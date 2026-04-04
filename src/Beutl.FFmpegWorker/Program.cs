using System.Diagnostics;
using System.IO.Pipes;
using Beutl.FFmpegIpc.Transport;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.FFmpegWorker;

internal static class Program
{
    private static readonly CancellationTokenSource s_shutdownCts = new();

    static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        int parentPid = -1;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipeName = args[++i];
                    break;
                case "--parent":
                    if (!int.TryParse(args[++i], out parentPid))
                    {
                        Console.Error.WriteLine($"Invalid parent PID: {args[i]}");
                        return 1;
                    }
                    break;
            }
        }

        if (pipeName == null)
        {
            Console.Error.WriteLine("Usage: Beutl.FFmpegWorker --pipe <name> --parent <pid>");
            return 1;
        }

        // ロギング初期化
        Log.LoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
#if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Information);
#endif
        });

        // FFmpeg初期化
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize FFmpeg: {ex.Message}");
            return 2;
        }

        // 親プロセス監視（終了時はCancellationTokenでグレースフルシャットダウン）
        if (parentPid > 0)
        {
            _ = Task.Run(() => MonitorParent(parentPid));
        }

        // パイプ接続
        using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipeClient.ConnectAsync(30000);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Failed to connect to parent process pipe within 30 seconds.");
            return 3;
        }

        using var connection = new IpcConnection(pipeClient);

        // ハンドシェイク送信（プロトコルバージョン含む）
        await connection.SendAsync(
            Beutl.FFmpegIpc.Protocol.IpcMessage.Create(0, Beutl.FFmpegIpc.Protocol.MessageType.HandshakeAck,
                new Beutl.FFmpegIpc.Protocol.Messages.HandshakeMessage()));

        // メッセージループ
        using var host = new WorkerHost(connection);
        await host.RunAsync(s_shutdownCts.Token);

        return 0;
    }

    private static void MonitorParent(int parentPid)
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch (ArgumentException)
        {
            // 親プロセスが存在しない
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error monitoring parent process {parentPid}: {ex.Message}");
        }

        // CancellationTokenでグレースフルにシャットダウン
        try { s_shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
