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
#if FFMPEG_OUT_OF_PROCESS
        if (FFmpegInstallNotifier.IsLibrariesMissing)
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

            // stdout/stderrを非同期に消費してバッファ溢れによるデッドロックを防止しつつ、
            // 受信した出力をホスト側ロガーへ転送する
            _process.ErrorDataReceived += (_, e) => LogWorkerOutput("stderr", e.Data);
            _process.OutputDataReceived += (_, e) => LogWorkerOutput("stdout", e.Data);
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

        _connection = new IpcConnection(pipeServer);

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

    private static void LogWorkerOutput(string channel, string? data)
    {
        if (data == null)
            return;

        // Process.*DataReceived ハンドラから例外を漏らすとホストプロセスが UnhandledException で落ちるため、
        // ロガー基盤（Serilog/OTLP）の障害は最終フォールバックの Console.Error で握りつぶす
        try
        {
            var (level, message) = ParseLevel(channel, data);
            s_logger.Log(level, "{Channel} {Message}", channel, message);
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[FFmpegWorker:{channel}] log dispatch failed: {ex}"); }
            catch { /* stderrまで死んでいたら諦める */ }
        }
    }

    private static (LogLevel Level, string Message) ParseLevel(string channel, string data)
    {
        // ワーカー側の FFmpegLoaderWorker.SetupLogging は FFmpeg ライブラリの Warning 以上のログのみ
        // "[ffmpeg:<Level>] ..." 形式で stderr に書き出す。プレフィックス付き行はそれを解釈し、
        // 非プレフィックス行（バージョンバナーや handler 由来の Console.Error 出力等）は
        // チャネルベースのフォールバック（stderr=Warning / stdout=Information）扱いにする。
        const string prefix = "[ffmpeg:";
        if (data.StartsWith(prefix, StringComparison.Ordinal))
        {
            int end = data.IndexOf(']', prefix.Length);
            if (end > prefix.Length)
            {
                string levelText = data.AsSpan(prefix.Length, end - prefix.Length).ToString();
                LogLevel level = levelText switch
                {
                    "Verbose" or "Trace" => LogLevel.Trace,
                    "Debug" => LogLevel.Debug,
                    "Info" or "Information" => LogLevel.Information,
                    "Warning" => LogLevel.Warning,
                    "Error" => LogLevel.Error,
                    "Fatal" or "Panic" => LogLevel.Critical,
                    _ => channel == "stderr" ? LogLevel.Warning : LogLevel.Information,
                };
                string message = data.Substring(end + 1).TrimStart();
                return (level, message);
            }
        }

        return (channel == "stderr" ? LogLevel.Warning : LogLevel.Information, data);
    }

    private static void ConfigureWorkerProcess(ProcessStartInfo startInfo)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Beutl.FFmpegWorker");

        if (OperatingSystem.IsWindows())
        {
            path += ".exe";
        }

        if (File.Exists(path))
        {
            startInfo.FileName = path;
        }
        else
        {
            // DLL mode: use dotnet host
            string dllPath = Path.ChangeExtension(path, ".dll");
            if (!File.Exists(dllPath) && !path.EndsWith(".exe"))
                dllPath = path + ".dll";

            string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            startInfo.FileName = dotnetHost;
            startInfo.ArgumentList.Insert(0, dllPath);
        }
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
