using System.Runtime.InteropServices;
using System.Threading.Channels;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Extensions.FFmpeg;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Bitmap = Beutl.Media.Bitmap;
using BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Stopwatch _mediaClock = Stopwatch.StartNew();
    private PendingFrame? _latest;
    private bool _hdrUnavailable;

    internal void Publish(Ref<Bitmap>? image, UIToneMappingOperator toneMapping, float exposure, bool playing, double time = 0, long playbackId = 0)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || _relayClients.Length == 0) return;
            if (image?.TryClone() is not { } reference) return;
            // Republishing a frame (for a new viewer or tone mapping) must not rewind audio.
            double clock = _latest is { } previous && previous.Time == time
                && previous.PlaybackId == playbackId && previous.Playing == playing
                ? previous.Clock : _mediaClock.Elapsed.TotalSeconds;
            _latest?.Dispose();
            _latest = new(reference, toneMapping, exposure, playing, time, playbackId, clock);
            _updates.Writer.TryWrite(true);
        }
    }

    private async Task EncodeFramesAsync()
    {
        var encoders = new Dictionary<bool, (int Width, int Height, PreviewEncoder Encoder)>();
        var cadence = Stopwatch.StartNew();
        try
        {
            while (await _updates.Reader.WaitToReadAsync(_stop.Token))
            {
                var remaining = TimeSpan.FromSeconds(1d / 15) - cadence.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _stop.Token);
                PendingFrame? frame;
                bool[] profiles;
                lock (_gate)
                {
                    _updates.Reader.TryRead(out _);
                    frame = _latest?.Snapshot();
                    profiles = _peers.Values.Distinct().Where(hdr => !hdr || !_hdrUnavailable).ToArray();
                }
                if (frame is null) continue;
                using (frame)
                {
                    // A keyframe requested while paused is a new RTP frame, even with unchanged pixels.
                    uint timestamp = (uint)(_mediaClock.Elapsed.TotalSeconds * 90000);
                    bool keyFrame = Interlocked.Exchange(ref _keyFrame, 0) != 0;
                    var (width, height) = FrameSize(frame.Image.Value);
                    foreach (bool hdr in profiles)
                    {
                        try
                        {
                            bool reset = !encoders.TryGetValue(hdr, out var entry) || entry.Width != width || entry.Height != height;
                            if (reset)
                            {
                                entry.Encoder?.Dispose();
                                entry = (width, height, new PreviewEncoder(width, height, hdr));
                                encoders[hdr] = entry;
                            }
                            byte[] pixels = ConvertFrame(frame.Image.Value, hdr, frame.ToneMapping, frame.Exposure);
                            byte[] data = await entry.Encoder.EncodeAsync(pixels, keyFrame || reset, _stop.Token).ConfigureAwait(false);
                            if (data.Length != 0 && _transport is { } transport)
                                transport.SendMedia(data, timestamp, video: true, hdr);
                        }
                        catch (Exception ex) when (hdr && !_stop.IsCancellationRequested)
                        {
                            // A build without 10-bit VP9 can still provide the normal SDR preview.
                            Log.CreateLogger<MobilePreviewServer>().LogWarning(ex, "HDR preview encoding is unavailable.");
                            if (encoders.Remove(hdr, out var failed)) failed.Encoder.Dispose();
                            lock (_gate)
                            {
                                _hdrUnavailable = true;
                                foreach (string id in _peers.Where(p => p.Value).Select(p => p.Key))
                                    SendToViewer(id, "{\"type\":\"error\",\"message\":\"hdr-unavailable\"}");
                            }
                        }
                    }
                }
                cadence.Restart();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.CreateLogger<MobilePreviewServer>().LogError(ex, "Could not encode mobile preview.");
            OnEncodingFailed();
        }
        finally { foreach (var entry in encoders.Values) entry.Encoder.Dispose(); }
    }

    internal static (int Width, int Height) FrameSize(Bitmap bitmap)
    {
        float scale = Math.Min(1f, 1280f / Math.Max(bitmap.Width, bitmap.Height));
        return (Math.Max(2, (int)(bitmap.Width * scale) / 2 * 2), Math.Max(2, (int)(bitmap.Height * scale) / 2 * 2));
    }

    internal static byte[] ConvertFrame(Bitmap bitmap, bool hdr, UIToneMappingOperator toneMapping, float exposure)
    {
        var (width, height) = FrameSize(bitmap);
        if (hdr)
        {
            // Resize in floating-point linear light. Converting to PQ only after resizing preserves
            // values above SDR white; Skia’s PQ transfer already uses 203 nit reference white.
            using var resized = new Bitmap(width, height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
            using (var canvas = new SKCanvas(resized.SKBitmap))
            using (var image = SKImage.FromBitmap(bitmap.SKBitmap))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear));
            }
            var pq = BitmapColorSpaceMapping.BuildTargetColorSpace(BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);
            using var converted = resized.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, pq);
            var result = new byte[converted.ByteCount];
            Marshal.Copy(converted.Data, result, 0, result.Length);
            return result;
        }
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        PreviewFrameRenderer.Draw(surface.Canvas, bitmap, SKRect.Create(bitmap.Width, bitmap.Height),
            SKRect.Create(width, height), BitmapInterpolationMode.HighQuality,
            toneMapping, exposure);
        using var pixels = surface.PeekPixels();
        var bytes = new byte[width * height * 4];
        Marshal.Copy(pixels.GetPixels(), bytes, 0, bytes.Length);
        return bytes;
    }

    internal sealed record PendingFrame(Ref<Bitmap> Image, UIToneMappingOperator ToneMapping, float Exposure,
        bool Playing, double Time, long PlaybackId, double Clock) : IDisposable
    {
        internal PendingFrame Snapshot() => this with { Image = Image.Clone() };
        public void Dispose() => Image.Dispose();
    }
}
