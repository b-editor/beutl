using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.WindowCapture;

internal sealed class WindowCaptureSession : IAsyncDisposable
{
    private const int BufferPoolSize = 3;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger _logger = Log.CreateLogger<WindowCaptureSession>();
    private readonly Window _window;
    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly int _frameRate;
    private readonly string _outputPath;
    private readonly string _ffmpegPath;
    private readonly RenderTargetBitmap _rtb;
    private readonly string _ffmpegPixelFormat;
    private readonly ConcurrentQueue<byte[]> _freeBuffers = new();
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private DispatcherTimer? _timer;
    private Process? _process;
    private Task? _writerTask;
    private long _droppedFrames;
    private long _capturedFrames;
    private bool _started;
    private bool _stopped;

    public WindowCaptureSession(Window window, double scale, int frameRate, string outputPath, string ffmpegPath)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (frameRate <= 0) throw new ArgumentOutOfRangeException(nameof(frameRate));

        _window = window;
        _frameRate = frameRate;
        _outputPath = outputPath;
        _ffmpegPath = ffmpegPath;

        Size clientSize = window.ClientSize;
        _width = Math.Max(2, (int)Math.Round(clientSize.Width * scale));
        _height = Math.Max(2, (int)Math.Round(clientSize.Height * scale));
        // libx264 requires even dimensions for yuv420p output
        if ((_width & 1) == 1) _width++;
        if ((_height & 1) == 1) _height++;
        _stride = _width * 4;

        var pixelSize = new PixelSize(_width, _height);
        var dpi = new Vector(96.0 * scale, 96.0 * scale);
        _rtb = new RenderTargetBitmap(pixelSize, dpi);
        PixelFormat fmt = _rtb.Format ?? PixelFormats.Bgra8888;
        if (fmt == PixelFormats.Bgra8888)
        {
            _ffmpegPixelFormat = "bgra";
        }
        else if (fmt == PixelFormats.Rgba8888)
        {
            _ffmpegPixelFormat = "rgba";
        }
        else
        {
            _rtb.Dispose();
            throw new NotSupportedException(
                $"Unsupported RenderTargetBitmap pixel format: {fmt}. Expected Bgra8888 or Rgba8888.");
        }

        int byteCount = _stride * _height;
        for (int i = 0; i < BufferPoolSize; i++)
        {
            _freeBuffers.Enqueue(new byte[byteCount]);
        }
    }

    public int Width => _width;
    public int Height => _height;
    public int FrameRate => _frameRate;
    public string OutputPath => _outputPath;
    public long CapturedFrameCount => Interlocked.Read(ref _capturedFrames);
    public long DroppedFrameCount => Interlocked.Read(ref _droppedFrames);

    public void Start()
    {
        if (_started) throw new InvalidOperationException("Session already started.");
        _started = true;

        _process = StartFFmpegProcess();
        _writerTask = Task.Run(WriterLoopAsync);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / _frameRate),
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _logger.LogInformation(
            "Window capture started: {Width}x{Height} @ {Fps}fps -> {Output} (ffmpeg pid={Pid})",
            _width, _height, _frameRate, _outputPath, _process.Id);
    }

    public async Task StopAsync()
    {
        if (!_started || _stopped) return;
        _stopped = true;

        if (_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnTimerTick;
        }

        _channel.Writer.TryComplete();

        if (_writerTask is { } wt)
        {
            try { await wt.ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Window capture writer task ended with error."); }
        }

        if (_process is { } proc)
        {
            try
            {
                if (!proc.HasExited)
                    proc.StandardInput.Close();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to close ffmpeg stdin."); }

            try
            {
                using var cts = new CancellationTokenSource(StopTimeout);
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ffmpeg did not exit within {Timeout}; killing.", StopTimeout);
                try { proc.Kill(entireProcessTree: true); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to kill ffmpeg."); }
            }

            int exitCode = proc.HasExited ? proc.ExitCode : -1;
            _logger.LogInformation(
                "Window capture stopped: captured={Captured}, dropped={Dropped}, ffmpeg exit={ExitCode}",
                CapturedFrameCount, DroppedFrameCount, exitCode);

            proc.Dispose();
            _process = null;
        }

        _rtb.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_stopped) return;

        if (!_freeBuffers.TryDequeue(out byte[]? buffer))
        {
            Interlocked.Increment(ref _droppedFrames);
            return;
        }

        try
        {
            _rtb.Render(_window);
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    _rtb.CopyPixels(
                        new PixelRect(0, 0, _width, _height),
                        (IntPtr)p,
                        buffer.Length,
                        _stride);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render frame; skipping.");
            _freeBuffers.Enqueue(buffer);
            Interlocked.Increment(ref _droppedFrames);
            return;
        }

        if (!_channel.Writer.TryWrite(buffer))
        {
            _freeBuffers.Enqueue(buffer);
            Interlocked.Increment(ref _droppedFrames);
        }
    }

    private async Task WriterLoopAsync()
    {
        Process? proc = _process;
        if (proc is null) return;

        Stream stdin = proc.StandardInput.BaseStream;
        try
        {
            await foreach (byte[] frame in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (proc.HasExited) break;
                    await stdin.WriteAsync(frame.AsMemory(0, _stride * _height)).ConfigureAwait(false);
                    Interlocked.Increment(ref _capturedFrames);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "ffmpeg stdin write failed; ending capture.");
                    break;
                }
                finally
                {
                    _freeBuffers.Enqueue(frame);
                }
            }

            try { await stdin.FlushAsync().ConfigureAwait(false); }
            catch { /* ignored */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writer loop terminated unexpectedly.");
        }
    }

    private Process StartFFmpegProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add(_ffmpegPixelFormat);
        startInfo.ArgumentList.Add("-video_size");
        startInfo.ArgumentList.Add($"{_width}x{_height}");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(_frameRate.ToString());
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("-");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("ultrafast");
        startInfo.ArgumentList.Add("-tune");
        startInfo.ArgumentList.Add("zerolatency");
        startInfo.ArgumentList.Add(_outputPath);

        Process proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg process.");

        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogDebug("ffmpeg: {Line}", e.Data);
        };
        proc.OutputDataReceived += (_, _) => { /* drain to avoid buffer stall */ };
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        return proc;
    }
}
