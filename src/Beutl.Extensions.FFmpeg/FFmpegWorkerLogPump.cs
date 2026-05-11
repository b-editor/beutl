using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

// ワーカーの stdout/stderr をホスト側ロガーへ転送する。
internal sealed class FFmpegWorkerLogPump : IDisposable
{
    private const int DefaultCapacity = 1024;

    private readonly ILogger _logger = Log.CreateLogger("FFmpegWorker");
    private readonly Channel<(string Channel, string Data)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;
    private int _disposed;

    public FFmpegWorkerLogPump(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    public void Attach(Process process)
    {
        process.ErrorDataReceived += (_, e) => Enqueue("stderr", e.Data);
        process.OutputDataReceived += (_, e) => Enqueue("stdout", e.Data);
    }

    private void Enqueue(string channel, string? data)
    {
        if (data == null)
            return;

        // BoundedChannel + DropOldest なら TryWrite は常に成功する。
        // ハンドラから例外を漏らすとホストプロセスが UnhandledException で落ちるため握りつぶす。
        try
        {
            _channel.Writer.TryWrite((channel, data));
        }
        catch
        {
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var (channel, data) in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                Dispatch(channel, data);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[FFmpegWorker] log pump terminated unexpectedly: {ex}"); }
            catch { }
        }
    }

    private void Dispatch(string channel, string data)
    {
        try
        {
            var (level, message) = ParseLevel(channel, data);
            _logger.Log(level, "{Channel} {Message}", channel, message);
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"[FFmpegWorker:{channel}] log dispatch failed: {ex}"); }
            catch { }
        }
    }

    private static (LogLevel Level, string Message) ParseLevel(string channel, string data)
    {
        // ワーカーは ILogger / FFmpeg ネイティブログ / 自身のエラーメッセージのすべてを
        // stdout に "[ffmpeg:<Level>] ..." 形式 (1行=1ログイベント, 改行は \n エスケープ) で書き出す。
        // プレフィックス付き行はそのレベルを採用しメッセージを復号する。
        // プレフィックスのない出力や想定外に stderr に流れてきたネイティブ出力は
        // チャネルベースのフォールバック (stderr=Warning / stdout=Information) で扱う。
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
                    "Fatal" or "Panic" or "Critical" => LogLevel.Critical,
                    _ => channel == "stderr" ? LogLevel.Warning : LogLevel.Information,
                };
                string message = DecodeMessage(data.AsSpan(end + 1).TrimStart());
                return (level, message);
            }
        }

        return (channel == "stderr" ? LogLevel.Warning : LogLevel.Information, data);
    }

    private static string DecodeMessage(ReadOnlySpan<char> encoded)
    {
        if (encoded.IndexOf('\\') < 0)
            return encoded.ToString();

        var sb = new StringBuilder(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            char c = encoded[i];
            if (c == '\\' && i + 1 < encoded.Length)
            {
                char next = encoded[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        try { _consumer.Wait(TimeSpan.FromSeconds(2)); }
        catch { }

        _cts.Cancel();
        _cts.Dispose();
    }
}
