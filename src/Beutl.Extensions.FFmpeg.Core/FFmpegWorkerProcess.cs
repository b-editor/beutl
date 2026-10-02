using System.Diagnostics;
using System.IO.Pipes;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

public sealed class FFmpegWorkerProcess : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger("FFmpegWorker");
    private static readonly Lazy<FFmpegWorkerProcess> s_decodingInstance = new(() => new FFmpegWorkerProcess(multiplexed: true));
    public static FFmpegWorkerProcess DecodingInstance => s_decodingInstance.Value;

    public static FFmpegWorkerProcess CreateForEncoding() => new(multiplexed: false);

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private volatile bool _disposed;
    private int _activeCalls;
    private readonly bool _multiplexed;
    private readonly Action<ProcessStartInfo> _configureWorkerStart;
    private Process? _process;
    private IpcConnection? _connection;
    private volatile bool _isReady;
    private FFmpegWorkerLogPump? _logPump;
    private string? _pipeName;
    private Exception? _lastStartupFailure;
    private long _retryStartupAt;

    public FFmpegWorkerProcess(bool multiplexed = false) : this(multiplexed, ConfigureWorkerProcess)
    {
    }

    internal FFmpegWorkerProcess(bool multiplexed, Action<ProcessStartInfo> configureWorkerStart)
    {
        _multiplexed = multiplexed;
        _configureWorkerStart = configureWorkerStart;
    }

    public bool IsRunning
    {
        get
        {
            lock (_lifetimeGate)
                return _isReady && _process is { HasExited: false } && _connection?.IsConnected == true;
        }
    }

    public int WorkerPid
    {
        get
        {
            lock (_lifetimeGate)
                return _process?.Id ?? 0;
        }
    }

    public IpcConnection EnsureStarted()
    {
        CancellationToken lifetimeToken = Enter();
        try
        {
            if (TryGetReadyConnection() is { } connection)
                return connection;

            ThrowIfLibrariesMissing();
            _startLock.Wait(lifetimeToken);
            try
            {
                if (TryGetReadyConnection() is { } readyConnection)
                    return readyConnection;

                ThrowIfLibrariesMissing();
                StartWorkerWithCooldownAsync(lifetimeToken).GetAwaiter().GetResult();
                return GetStartedConnection();
            }
            finally
            {
                _startLock.Release();
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(FFmpegWorkerProcess));
        }
        finally
        {
            Exit();
        }
    }

    public async Task<IpcConnection> EnsureStartedAsync(CancellationToken ct = default)
    {
        CancellationToken lifetimeToken = Enter();
        try
        {
            if (TryGetReadyConnection() is { } connection)
                return connection;

            ThrowIfLibrariesMissing();
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
            await _startLock.WaitAsync(startupCancellation.Token).ConfigureAwait(false);
            try
            {
                if (TryGetReadyConnection() is { } readyConnection)
                    return readyConnection;

                ThrowIfLibrariesMissing();
                await StartWorkerWithCooldownAsync(startupCancellation.Token).ConfigureAwait(false);
                return GetStartedConnection();
            }
            finally
            {
                _startLock.Release();
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(FFmpegWorkerProcess));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        finally
        {
            Exit();
        }
    }

    private CancellationToken Enter()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeCalls++;
            return _lifetimeCancellation.Token;
        }
    }

    private void Exit()
    {
        bool cleanup;
        lock (_lifetimeGate)
            cleanup = --_activeCalls == 0 && _disposed;

        if (cleanup)
            CompleteDisposal();
    }

    private IpcConnection? TryGetReadyConnection()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _isReady && _process is { HasExited: false } ? _connection : null;
        }
    }

    private IpcConnection GetStartedConnection()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _connection!;
        }
    }

    private async Task StartWorkerWithCooldownAsync(CancellationToken ct)
    {
        if (_lastStartupFailure is not null && Environment.TickCount64 < _retryStartupAt)
            throw new InvalidOperationException("FFmpeg worker startup failed recently; retry after the cooldown.", _lastStartupFailure);
        try
        {
            await StartWorkerAsync(ct).ConfigureAwait(false);
            _lastStartupFailure = null;
        }
        catch (Exception ex)
        {
            try { Cleanup(); }
            catch (Exception cleanup) { s_logger.LogWarning(cleanup, "Failed to clean up an unsuccessful FFmpeg worker start."); }
            if (!_disposed && ex is not OperationCanceledException and not FFmpegLibrariesNotFoundException)
            {
                _lastStartupFailure = ex;
                _retryStartupAt = Environment.TickCount64 + 30_000;
            }
            throw;
        }

#if !BEUTL_FFMPEG_WORKER
        // Observer failures still reach this caller, but must not tear down a ready shared worker.
        // Availability callbacks may re-enter EnsureStarted, so publish readiness before notifying.
        FFmpegLibraryState.NotifyWorkerStarted();
#endif
    }

    private static void ThrowIfLibrariesMissing()
    {
#if !BEUTL_FFMPEG_WORKER
        // Short-circuit only inside the re-probe cooldown; once it elapses, let the start attempt run
        // so its outcome re-probes real availability instead of trusting the sticky missing flag.
        if (FFmpegLibraryState.ShouldSkipStartProbe(Environment.TickCount64))
        {
            throw new FFmpegLibrariesNotFoundException(
                "FFmpeg libraries are missing; install FFmpeg before starting the worker.");
        }
#endif
    }

    private async Task StartWorkerAsync(CancellationToken ct)
    {
        Cleanup();

        // macOSの場合、Unix Domain Socketが使われる。その際のパスの長さ制限を考慮して、パイプ名は短くする。
        _pipeName = $"beutl-ff-{Guid.NewGuid():N}";

        var pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            // ワーカープロセス起動
            var startInfo = new ProcessStartInfo();
            _configureWorkerStart(startInfo);
            ct.ThrowIfCancellationRequested();
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(_pipeName);
            startInfo.ArgumentList.Add("--parent");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start FFmpeg worker process");

            // stdout/stderr のドレインはストリームリーダースレッドから即時 enqueue するだけにし、
            // ロガーシンクへの実書き込みはバックグラウンドで行う (FFmpegWorkerLogPump 参照)。
            _logPump = new FFmpegWorkerLogPump();
            _logPump.Attach(_process);
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();

            // パイプ接続待機 + Worker早期終了検出
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(30));

            var connectTask = pipeServer.WaitForConnectionAsync(connectCts.Token);
            var exitTask = _process.WaitForExitAsync(connectCts.Token);
            var completed = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);

            if (completed == exitTask)
            {
                // キャンセル経由でexitTaskが完了した場合は OperationCanceledException を再スロー
                await exitTask.ConfigureAwait(false);

                int code = _process.ExitCode;

                // 敗者となった connectTask の例外を観測しておく（UnobservedTaskException 防止）
                connectCts.Cancel();
                _ = connectTask.ContinueWith(
                    static t => { _ = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                pipeServer.Dispose();
                if (code == 2)
                {
#if !BEUTL_FFMPEG_WORKER
                    // A real probe observed the libraries missing: throttle the next probe.
                    FFmpegLibraryState.ArmReprobeCooldown();
#endif
                    throw new FFmpegLibrariesNotFoundException(
                        "FFmpeg worker exited because the FFmpeg libraries could not be found.");
                }
                throw new InvalidOperationException(
                    $"FFmpeg worker exited unexpectedly with code {code} before establishing connection.");
            }

            // 接続が先に成立。例外があれば伝播させる
            await connectTask.ConfigureAwait(false);
            // 敗者となった exitTask の例外を観測しておく（UnobservedTaskException 防止）
            _ = exitTask.ContinueWith(
                static t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (OperationCanceledException)
        {
            if (_process != null)
            {
                try { _process.Kill(); }
                catch (InvalidOperationException) { }
            }
            pipeServer.Dispose();
            throw CreateWorkerStartCanceledException(ct);
        }
        catch
        {
            pipeServer.Dispose();
            throw;
        }

        _connection = new IpcConnection(pipeServer)
        {
            // 受信ループ / Dispose 異常系の診断は ILogger に転送する。
            // LogError(Exception?, ...) は ex が null でも受け付ける。
            DiagnosticLogger = (msg, ex) => s_logger.LogError(ex, "{Message}", msg),
            // 現状ホスト側のリードはキャンセルトークンを渡しておらず、
            // 共有メモリは参照カウントで管理されるため通常は呼ばれない。
            // 将来 CancellationToken 対応リードを追加した際のリグレッション検知として
            // 観測のみログに残す (実際のバッファ解放は消費側の責務とする)。
            DroppedResponseHandler = msg => s_logger.LogWarning(
                "Dropped IPC response Id={Id} Type={Type}; no awaiter present.", msg.Id, msg.Type)
        };

        // ハンドシェイク待機（プロトコルバージョン検証）
        var handshake = await _connection.ReceiveAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Worker closed connection during handshake");

        if (handshake.Type != MessageType.HandshakeAck)
            throw new InvalidOperationException($"Invalid handshake: expected HandshakeAck, got {handshake.Type}");

        var handshakePayload = handshake.GetPayload<HandshakeMessage>();
        if (handshakePayload != null && handshakePayload.ProtocolVersion != ProtocolConstants.CurrentVersion)
            throw new InvalidOperationException(
                $"Protocol version mismatch: host={ProtocolConstants.CurrentVersion}, worker={handshakePayload.ProtocolVersion}");

        // デコード用接続は多重化モードで起動（複数リーダーからの並行リクエスト対応）
        // Startup cancellation belongs to this caller; the shared receive loop lives until connection disposal.
        if (_multiplexed)
            _connection.StartMultiplexedReceive();

        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _isReady = true;
        }
    }

    internal static Exception CreateWorkerStartCanceledException(CancellationToken ct)
    {
        return ct.IsCancellationRequested
            ? new OperationCanceledException(ct)
            : new TimeoutException("FFmpeg worker failed to connect within 30 seconds");
    }

    private static void ConfigureWorkerProcess(ProcessStartInfo startInfo)
    {
        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        WorkerCommand command = ResolveWorkerCommand(
            AppContext.BaseDirectory, OperatingSystem.IsWindows(), dotnetHost, File.Exists);

        startInfo.FileName = command.FileName;
        if (command.DllArgument is { } dll)
        {
            // DLL mode: pass the worker assembly as the first argument to the dotnet host.
            startInfo.ArgumentList.Insert(0, dll);
        }
    }

    internal readonly record struct WorkerCommand(string FileName, string? DllArgument);

    /// <summary>
    /// Resolves how to launch the GPL worker process. Dev builds isolate it under an
    /// <c>FFmpegWorker/</c> subdirectory (so it never overwrites the app's shared assemblies); Nuke
    /// publishes lay it out flat. Prefers the subdir over the flat layout, and an apphost over a bare
    /// <c>.dll</c>; when no apphost exists, launches via the dotnet host with the .dll as the first arg.
    /// </summary>
    internal static WorkerCommand ResolveWorkerCommand(
        string baseDirectory, bool isWindows, string dotnetHost, Func<string, bool> fileExists)
    {
        string exeSuffix = isWindows ? ".exe" : "";
        string subDirStem = Path.Combine(baseDirectory, "FFmpegWorker", "Beutl.FFmpegWorker");
        string flatStem = Path.Combine(baseDirectory, "Beutl.FFmpegWorker");
        string stem = fileExists(subDirStem + exeSuffix) || fileExists(subDirStem + ".dll")
            ? subDirStem
            : flatStem;

        string apphostPath = stem + exeSuffix;
        return fileExists(apphostPath)
            ? new WorkerCommand(apphostPath, null)
            : new WorkerCommand(dotnetHost, stem + ".dll");
    }

    /// <summary>Reports whether a launchable Beutl.FFmpegWorker is present under
    /// <paramref name="baseDirectory"/> (FFmpegWorker/ subdir, then flat layout).</summary>
    public static bool IsWorkerAvailable(string baseDirectory)
    {
        string exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        string subDirStem = Path.Combine(baseDirectory, "FFmpegWorker", "Beutl.FFmpegWorker");
        string flatStem = Path.Combine(baseDirectory, "Beutl.FFmpegWorker");
        return File.Exists(subDirStem + exeSuffix) || File.Exists(subDirStem + ".dll")
            || File.Exists(flatStem + exeSuffix) || File.Exists(flatStem + ".dll");
    }

    private void Cleanup()
    {
        IpcConnection? connection;
        Process? process;
        FFmpegWorkerLogPump? logPump;
        lock (_lifetimeGate)
        {
            _isReady = false;
            connection = _connection;
            _connection = null;
            process = _process;
            _process = null;
            logPump = _logPump;
            _logPump = null;
        }

        try
        {
            connection?.Dispose();
        }
        finally
        {
            try
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            try { process.Kill(); }
                            catch (InvalidOperationException) { }
                            catch (Exception ex)
                            {
                                s_logger.LogWarning(ex, "Failed to kill worker process");
                            }
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            finally
            {
                logPump?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _isReady = false;
            // Cancellation also owns a lease, since callbacks can complete startup inline.
            _activeCalls++;
        }

        try
        {
            _lifetimeCancellation.Cancel();
        }
        finally
        {
            Exit();
        }
    }

    private void CompleteDisposal()
    {
        try
        {
            if (_connection != null)
            {
                try
                {
                    _connection.SendAsync(
                        IpcMessage.CreateSimple(0, MessageType.Shutdown)).AsTask().Wait(3000);
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Graceful shutdown of FFmpeg worker failed");
                }
            }

        }
        finally
        {
            try
            {
                Cleanup();
            }
            finally
            {
                _startLock.Dispose();
                _lifetimeCancellation.Dispose();
            }
        }
    }
}
