using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Logging;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private PendingFrame? _pending;

    // Borrow the caller's ref. Only the newest frame waits for encoding; the render thread never
    // waits for compression or for a slow browser. No clients means no retained pixels or encoding.
    internal void Publish(Ref<Bitmap>? image, UIToneMappingOperator toneMapping, float exposure, bool playing, double time = 0, long playbackId = 0)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || (_relayClients?.Length ?? _clients.Count) == 0) return;
            if (image?.TryClone() is not { } reference) return;
            _pending?.Dispose();
            _pending = new PendingFrame(reference, toneMapping, exposure, playing, time, playbackId);
            _updates.Writer.TryWrite(true);
        }
    }

    private async Task EncodeFramesAsync()
    {
        var nextFrame = Stopwatch.StartNew();
        try
        {
            while (await _updates.Reader.WaitToReadAsync(_stop.Token))
            {
                // At most 15 fps; a paused scene is encoded only when its image/settings change.
                var remaining = TimeSpan.FromSeconds(1d / 15) - nextFrame.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _stop.Token);
                PendingFrame? frame;
                lock (_gate)
                {
                    _updates.Reader.TryRead(out _);
                    frame = _pending;
                    _pending = null;
                }
                if (frame is null) continue;
                using (frame)
                {
                    try
                    {
                        byte[] jpeg = EncodeJpeg(frame.Image.Value, frame.ToneMapping, frame.Exposure);
                        byte[] encoded = PreviewMediaPacket.Video(jpeg, frame.Playing, frame.Time, frame.PlaybackId);
                        lock (_gate)
                            foreach (var client in _clients.Values) client.Frames.Writer.TryWrite(encoded);
                    }
                    catch (Exception ex)
                    {
                        Log.CreateLogger<MobilePreviewServer>().LogError(ex, "Could not encode a mobile preview frame.");
                    }
                }
                nextFrame.Restart();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    internal static byte[] EncodeJpeg(Bitmap bitmap, UIToneMappingOperator toneMapping, float exposure)
    {
        float scale = Math.Min(1f, 1280f / Math.Max(bitmap.Width, bitmap.Height));
        int width = Math.Max(1, (int)MathF.Round(bitmap.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(bitmap.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        PreviewFrameRenderer.Draw(surface.Canvas, bitmap, SKRect.Create(bitmap.Width, bitmap.Height),
            SKRect.Create(width, height), BitmapInterpolationMode.HighQuality, toneMapping, exposure);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    private sealed record PendingFrame(Ref<Bitmap> Image, UIToneMappingOperator ToneMapping, float Exposure, bool Playing, double Time, long PlaybackId)
        : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

}
