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
    private readonly bool _multiplexed;
    private Process? _process;
    private IpcConnection? _connection;
    private FFmpegWorkerLogPump? _logPump;
    private string? _pipeName;

    public FFmpegWorkerProcess(bool multiplexed = false)
    {
        _multiplexed = multiplexed;
    }

    public bool IsRunning => _process is { HasExited: false } && _connection?.IsConnected == true;

    public int WorkerPid => _process?.Id ?? 0;

    public IpcConnection EnsureStarted()
    {
        if (_connection != null && _process is { HasExited: false })
            return _connection;

        ThrowIfLibrariesMissing();

        _startLock.Wait();
        try
        {
            if (_connection != null && _process is { HasExited: false })
                return _connection;

            ThrowIfLibrariesMissing();

            // 同期コンテキストから非同期メソッドを呼び出す（タイムアウト付き）
            StartWorkerAsync(CancellationToken.None).GetAwaiter().GetResult();
            return _connection!;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<IpcConnection> EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_connection != null && _process is { HasExited: false })
            return _connection;

        ThrowIfLibrariesMissing();

        await _startLock.WaitAsync(ct);
        try
        {
            if (_connection != null && _process is { HasExited: false })
                return _connection;

            ThrowIfLibrariesMissing();

            await StartWorkerAsync(ct);
            return _connection!;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private static void ThrowIfLibrariesMissing()
    {
#if !BEUTL_FFMPEG_WORKER
        if (FFmpegLibraryState.IsLibrariesMissing)
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
            ConfigureWorkerProcess(startInfo);
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
                await exitTask;

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
                    throw new FFmpegLibrariesNotFoundException(
                        "FFmpeg worker exited because the FFmpeg libraries could not be found.");
                }
                throw new InvalidOperationException(
                    $"FFmpeg worker exited unexpectedly with code {code} before establishing connection.");
            }

            // 接続が先に成立。例外があれば伝播させる
            await connectTask;
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
            throw new TimeoutException("FFmpeg worker failed to connect within 30 seconds");
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
        var handshake = await _connection.ReceiveAsync(ct)
            ?? throw new InvalidOperationException("Worker closed connection during handshake");

        if (handshake.Type != MessageType.HandshakeAck)
            throw new InvalidOperationException($"Invalid handshake: expected HandshakeAck, got {handshake.Type}");

        var handshakePayload = handshake.GetPayload<HandshakeMessage>();
        if (handshakePayload != null && handshakePayload.ProtocolVersion != ProtocolConstants.CurrentVersion)
            throw new InvalidOperationException(
                $"Protocol version mismatch: host={ProtocolConstants.CurrentVersion}, worker={handshakePayload.ProtocolVersion}");

        // デコード用接続は多重化モードで起動（複数リーダーからの並行リクエスト対応）
        if (_multiplexed)
            _connection.StartMultiplexedReceive(ct);
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
        _connection?.Dispose();
        _connection = null;

        if (_process != null)
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(); }
                catch (InvalidOperationException) { /* プロセスが既に終了 */ }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Failed to kill worker process");
                }
            }
            _process.Dispose();
            _process = null;
        }

        // プロセス Dispose 後にポンプを破棄することで、終了直前に届いた行も
        // バックグラウンド consumer が処理してから停止する。
        _logPump?.Dispose();
        _logPump = null;
    }

    public void Dispose()
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

        Cleanup();
        _startLock.Dispose();
    }
}
