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
                return IsReadyUnderGate();
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
            }
            finally
            {
                _startLock.Release();
            }
            NotifyWorkerStarted();
            _startLock.Wait(lifetimeToken);
            try
            {
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
            }
            finally
            {
                _startLock.Release();
            }
            NotifyWorkerStarted();
            await _startLock.WaitAsync(startupCancellation.Token).ConfigureAwait(false);
            try
            {
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
            return IsReadyUnderGate() ? _connection : null;
        }
    }

    // The caller holds _lifetimeGate.
    private bool IsReadyUnderGate()
        => _isReady && _process is { HasExited: false } && _connection?.IsConnected == true;

    private IpcConnection GetStartedConnection()
    {
        return TryGetReadyConnection() ?? throw new InvalidOperationException(
            "FFmpeg worker is unavailable after startup notification.", _lastStartupFailure);
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
            if (_disposed)
                throw new ObjectDisposedException(nameof(FFmpegWorkerProcess));
            if (ex is not OperationCanceledException and not FFmpegLibrariesNotFoundException)
            {
                _lastStartupFailure = ex;
                _retryStartupAt = Environment.TickCount64 + 30_000;
            }
            throw;
        }
    }

    private static void NotifyWorkerStarted()
    {
#if !BEUTL_FFMPEG_WORKER
        // Observer failures still reach this caller, but must not tear down a ready shared worker.
        // The public entry point has released its startup lock before observers can restart a worker.
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
        Process? process = null;
        FFmpegWorkerLogPump logPump;

        try
        {
            // ワーカープロセス起動
            var startInfo = CreateWorkerStartInfo(_pipeName, ct);

            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start FFmpeg worker process");
                _process = process;

                // Publish the process and log pump together so disposal can claim both.
                // User configuration and availability callbacks run outside this gate.
                _logPump = logPump = new FFmpegWorkerLogPump();
                logPump.Attach(process);
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }

            await WaitForWorkerConnectionAsync(pipeServer, process, logPump, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (process != null)
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
            }
            pipeServer.Dispose();
            throw CreateWorkerStartCanceledException(ct);
        }
        catch (Exception) when (_disposed)
        {
            pipeServer.Dispose();
            throw new ObjectDisposedException(nameof(FFmpegWorkerProcess));
        }
        catch
        {
            pipeServer.Dispose();
            throw;
        }

        IpcConnection connection;
        try
        {
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                connection = CreateConnection(pipeServer);
                _connection = connection;
            }
        }
        catch
        {
            pipeServer.Dispose();
            throw;
        }

        // ハンドシェイク待機（プロトコルバージョン検証）
        var handshake = await connection.ReceiveAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Worker closed connection during handshake");
        ValidateHandshake(handshake);

        // デコード用接続は多重化モードで起動（複数リーダーからの並行リクエスト対応）
        // Startup cancellation belongs to this caller; the shared receive loop lives until connection disposal.
        if (_multiplexed)
            connection.StartMultiplexedReceive();

        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _isReady = true;
        }
    }

    private ProcessStartInfo CreateWorkerStartInfo(string pipeName, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo();
        _configureWorkerStart(startInfo);
        ct.ThrowIfCancellationRequested();
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--parent");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        return startInfo;
    }

    private static async Task WaitForWorkerConnectionAsync(
        NamedPipeServerStream pipeServer, Process process, FFmpegWorkerLogPump logPump, CancellationToken ct)
    {
        // パイプ接続待機 + Worker早期終了検出
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(30));

        var connectTask = pipeServer.WaitForConnectionAsync(connectCts.Token);
        var exitTask = process.WaitForExitAsync(connectCts.Token);
        var completed = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);

        if (completed == exitTask)
        {
            // キャンセル経由でexitTaskが完了した場合は OperationCanceledException を再スロー
            await exitTask.ConfigureAwait(false);

            int code = process.ExitCode;
            // WaitForExitAsync drains the redirected streams before completing.
            string stderr = logPump.GetStandardErrorTail();
            string details = stderr.Length == 0 ? string.Empty : $"{Environment.NewLine}Worker stderr: {stderr}";

            // 敗者となった connectTask の例外を観測しておく（UnobservedTaskException 防止）
            connectCts.Cancel();
            ObserveFault(connectTask);

            pipeServer.Dispose();
            if (code == 2)
            {
#if !BEUTL_FFMPEG_WORKER
                // A real probe observed the libraries missing: throttle the next probe.
                FFmpegLibraryState.ArmReprobeCooldown();
#endif
                throw new FFmpegLibrariesNotFoundException(
                    "FFmpeg worker exited because the FFmpeg libraries could not be found." + details);
            }
            throw new InvalidOperationException(
                $"FFmpeg worker exited unexpectedly with code {code} before establishing connection." + details);
        }

        // 接続が先に成立。例外があれば伝播させる
        await connectTask.ConfigureAwait(false);
        // 敗者となった exitTask の例外を観測しておく（UnobservedTaskException 防止）
        ObserveFault(exitTask);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static IpcConnection CreateConnection(NamedPipeServerStream pipeServer)
    {
        return new IpcConnection(pipeServer)
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
    }

    private static void ValidateHandshake(IpcMessage handshake)
    {
        if (handshake.Type != MessageType.HandshakeAck)
            throw new InvalidOperationException($"Invalid handshake: expected HandshakeAck, got {handshake.Type}");

        var handshakePayload = handshake.GetPayload<HandshakeMessage>();
        if (handshakePayload != null && handshakePayload.ProtocolVersion != ProtocolConstants.CurrentVersion)
            throw new InvalidOperationException(
                $"Protocol version mismatch: host={ProtocolConstants.CurrentVersion}, worker={handshakePayload.ProtocolVersion}");
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
    /// publishes lay it out flat. Requires the worker DLL and its runtime configuration/dependency
    /// manifests; skips incomplete layouts. Prefers the subdir over the flat layout, and an apphost
    /// over DLL mode; when no apphost exists, launches the DLL via the dotnet host.
    /// </summary>
    internal static WorkerCommand ResolveWorkerCommand(
        string baseDirectory, bool isWindows, string dotnetHost, Func<string, bool> fileExists)
    {
        (string exeSuffix, string subDirStem, string flatStem) = GetWorkerStems(baseDirectory, isWindows);
        foreach (string stem in new[] { subDirStem, flatStem })
        {
            if (!IsWorkerDeploymentComplete(stem, fileExists))
                continue;

            string apphostPath = stem + exeSuffix;
            return fileExists(apphostPath)
                ? new WorkerCommand(apphostPath, null)
                : new WorkerCommand(dotnetHost, stem + ".dll");
        }

        throw new FileNotFoundException(
            "No complete FFmpeg worker deployment was found in FFmpegWorker/ or the application directory. "
            + "Each layout requires Beutl.FFmpegWorker.dll, Beutl.FFmpegWorker.runtimeconfig.json, "
            + "and Beutl.FFmpegWorker.deps.json.");
    }

    /// <summary>Reports whether a launchable Beutl.FFmpegWorker is present under
    /// <paramref name="baseDirectory"/> (FFmpegWorker/ subdir, then flat layout).</summary>
    public static bool IsWorkerAvailable(string baseDirectory)
    {
        (_, string subDirStem, string flatStem) = GetWorkerStems(baseDirectory, OperatingSystem.IsWindows());
        return IsWorkerDeploymentComplete(subDirStem, File.Exists)
            || IsWorkerDeploymentComplete(flatStem, File.Exists);
    }

    private static bool IsWorkerDeploymentComplete(string stem, Func<string, bool> fileExists)
        => fileExists(stem + ".dll")
            && fileExists(stem + ".runtimeconfig.json")
            && fileExists(stem + ".deps.json");

    private static (string ExeSuffix, string SubDirStem, string FlatStem) GetWorkerStems(
        string baseDirectory, bool isWindows)
    {
        string exeSuffix = isWindows ? ".exe" : "";
        string subDirStem = Path.Combine(baseDirectory, "FFmpegWorker", "Beutl.FFmpegWorker");
        string flatStem = Path.Combine(baseDirectory, "Beutl.FFmpegWorker");
        return (exeSuffix, subDirStem, flatStem);
    }

    private void Cleanup(bool graceful = false)
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
            try
            {
                if (graceful && connection != null)
                    RequestGracefulShutdown(connection);
            }
            finally
            {
                connection?.Dispose();
            }
        }
        finally
        {
            try
            {
                if (process != null)
                    KillAndDispose(process);
            }
            finally
            {
                logPump?.Dispose();
            }
        }
    }

    private static void RequestGracefulShutdown(IpcConnection connection)
    {
        try
        {
            connection.SendAsync(IpcMessage.CreateSimple(0, MessageType.Shutdown))
                .AsTask().Wait(3000);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Graceful shutdown of FFmpeg worker failed");
        }
    }

    private static void KillAndDispose(Process process)
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
            try
            {
                // Retire owned resources even if a synchronous availability observer is blocked.
                Cleanup(graceful: true);
            }
            finally
            {
                Exit();
            }
        }
    }

    private void CompleteDisposal()
    {
        try
        {
            _startLock.Dispose();
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }
}
