using System.Diagnostics;
using System.IO.Pipes;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.Extensions.FFmpeg;

public sealed class FFmpegWorkerProcess : IDisposable
{
    private static readonly Lazy<FFmpegWorkerProcess> s_decodingInstance = new();
    private static readonly Lazy<FFmpegWorkerProcess> s_encodingInstance = new();
    public static FFmpegWorkerProcess DecodingInstance => s_decodingInstance.Value;
    public static FFmpegWorkerProcess EncodingInstance => s_encodingInstance.Value;

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _process;
    private IpcConnection? _connection;
    private string? _pipeName;

    public bool IsRunning => _process is { HasExited: false } && _connection?.IsConnected == true;

    public int WorkerPid => _process?.Id ?? 0;

    public IpcConnection EnsureStarted()
    {
        if (_connection != null && _process is { HasExited: false })
            return _connection;

        _startLock.Wait();
        try
        {
            if (_connection != null && _process is { HasExited: false })
                return _connection;

            StartWorker();
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

        await _startLock.WaitAsync(ct);
        try
        {
            if (_connection != null && _process is { HasExited: false })
                return _connection;

            await StartWorkerAsync(ct);
            return _connection!;
        }
        finally
        {
            _startLock.Release();
        }
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

            // stderrを非同期に消費してバッファ溢れによるデッドロックを防止
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[FFmpegWorker] {e.Data}");
            };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[FFmpegWorker] {e.Data}");
            };
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();

            // パイプ接続待機
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(30));

            await pipeServer.WaitForConnectionAsync(connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (_process != null) { try { _process.Kill(); } catch { } }
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
    }

    private void StartWorker()
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

            // stderrを非同期に消費してバッファ溢れによるデッドロックを防止
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[FFmpegWorker] {e.Data}");
            };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[FFmpegWorker] {e.Data}");
            };
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();

            // パイプ接続待機
            pipeServer.WaitForConnection();
        }
        catch (OperationCanceledException)
        {
            if (_process != null) { try { _process.Kill(); } catch { } }
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
        var handshake = _connection.Receive()
            ?? throw new InvalidOperationException("Worker closed connection during handshake");

        if (handshake.Type != MessageType.HandshakeAck)
            throw new InvalidOperationException($"Invalid handshake: expected HandshakeAck, got {handshake.Type}");

        var handshakePayload = handshake.GetPayload<HandshakeMessage>();
        if (handshakePayload != null && handshakePayload.ProtocolVersion != ProtocolConstants.CurrentVersion)
            throw new InvalidOperationException(
                $"Protocol version mismatch: host={ProtocolConstants.CurrentVersion}, worker={handshakePayload.ProtocolVersion}");
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
                try { _process.Kill(); } catch { }
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
            catch { }
        }

        Cleanup();
        _startLock.Dispose();
    }
}
