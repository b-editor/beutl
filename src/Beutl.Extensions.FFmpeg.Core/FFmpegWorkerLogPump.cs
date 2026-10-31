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
    private const int StandardErrorTailCapacity = 4096;

    private readonly ILogger _logger = Log.CreateLogger("FFmpegWorker");
    private readonly Channel<(string Channel, string Data)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;
    private readonly StringBuilder _standardErrorTail = new(StandardErrorTailCapacity);
    private readonly object _standardErrorGate = new();
    private int _disposed;

    public FFmpegWorkerLogPump()
    {
        _channel = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(DefaultCapacity)
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
            // Capture before enqueueing so dropped log events cannot hide a startup failure.
            if (channel == "stderr")
            {
                lock (_standardErrorGate)
                {
                    int maxLineLength = StandardErrorTailCapacity - Environment.NewLine.Length;
                    _standardErrorTail.AppendLine(data.Length > maxLineLength ? data[^maxLineLength..] : data);
                    if (_standardErrorTail.Length > StandardErrorTailCapacity)
                        _standardErrorTail.Remove(0, _standardErrorTail.Length - StandardErrorTailCapacity);
                }
            }
            _channel.Writer.TryWrite((channel, data));
        }
        catch
        {
        }
    }

    public string GetStandardErrorTail()
    {
        lock (_standardErrorGate)
            return _standardErrorTail.ToString().Trim();
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

    internal static (LogLevel Level, string Message) ParseLevel(string channel, string data)
    {
        // Managed worker messages escape newlines in a tagged stdout line. FFmpeg's native
        // callback writes stderr directly, with optional context tags before its level tag.
        const string prefix = "[ffmpeg:";
        if (data.StartsWith(prefix, StringComparison.Ordinal))
        {
            int end = data.IndexOf(']', prefix.Length);
            if (end > prefix.Length)
            {
                string levelText = data.AsSpan(prefix.Length, end - prefix.Length).ToString();
                LogLevel level = levelText switch
                {
                    "Trace" => LogLevel.Trace,
                    "Debug" => LogLevel.Debug,
                    "Information" => LogLevel.Information,
                    "Warning" => LogLevel.Warning,
                    "Error" => LogLevel.Error,
                    "Critical" => LogLevel.Critical,
                    _ => channel == "stderr" ? LogLevel.Warning : LogLevel.Information,
                };
                string message = DecodeMessage(data.AsSpan(end + 1).TrimStart());
                return (level, message);
            }
        }

        if (channel == "stderr")
        {
            ReadOnlySpan<char> remaining = data.AsSpan().TrimStart();
            while (remaining.StartsWith("[", StringComparison.Ordinal))
            {
                int end = remaining.IndexOf(']');
                if (end < 0) break;
                LogLevel level = remaining[1..end] switch
                {
                    "trace" or "verbose" => LogLevel.Trace,
                    "debug" => LogLevel.Debug,
                    "info" => LogLevel.Information,
                    "warning" => LogLevel.Warning,
                    "error" => LogLevel.Error,
                    "fatal" or "panic" => LogLevel.Critical,
                    _ => LogLevel.None,
                };
                if (level != LogLevel.None) return (level, data);
                remaining = remaining[(end + 1)..].TrimStart();
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
