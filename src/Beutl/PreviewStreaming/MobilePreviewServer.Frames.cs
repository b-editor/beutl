using System.Runtime.InteropServices;
using System.Threading.Channels;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc.Protocol.Messages;
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
    private int _keyFrame = 1;

    private void RequestKeyFrame()
    {
        Interlocked.Exchange(ref _keyFrame, 1);
        _updates.Writer.TryWrite(true);
    }

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
        var encoders = new Dictionary<bool, (FrameConverter Converter, PreviewEncoder Encoder)>();
        var cadence = Stopwatch.StartNew();
        try
        {
            while (await _updates.Reader.WaitToReadAsync(_stop.Token))
            {
                var remaining = TimeSpan.FromSeconds(1d / PreviewEncodingFormat.FrameRate) - cadence.Elapsed;
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
                            bool reset = !encoders.TryGetValue(hdr, out var entry)
                                || entry.Converter.Width != width || entry.Converter.Height != height;
                            if (reset)
                            {
                                entry.Encoder?.Dispose();
                                entry.Converter?.Dispose();
                                var converter = new FrameConverter(width, height, hdr);
                                try
                                {
                                    entry = (converter, new PreviewEncoder(width, height, hdr));
                                }
                                catch
                                {
                                    converter.Dispose();
                                    throw;
                                }
                                encoders[hdr] = entry;
                            }
                            byte[] pixels = entry.Converter.Convert(frame.Image.Value, frame.ToneMapping, frame.Exposure);
                            byte[] data = await entry.Encoder.EncodeAsync(pixels, keyFrame || reset, _stop.Token).ConfigureAwait(false);
                            if (data.Length != 0 && _transport is { } transport)
                                transport.SendMedia(data, timestamp, video: true, hdr);
                        }
                        catch (Exception ex) when (hdr && !_stop.IsCancellationRequested)
                        {
                            // A build without 10-bit VP9 can still provide the normal SDR preview.
                            Log.CreateLogger<MobilePreviewServer>().LogWarning(ex, "HDR preview encoding is unavailable.");
                            if (encoders.Remove(hdr, out var failed))
                            {
                                failed.Encoder.Dispose();
                                failed.Converter.Dispose();
                            }
                            lock (_gate)
                            {
                                _hdrUnavailable = true;
                                foreach (string id in _peers.Where(p => p.Value).Select(p => p.Key))
                                    SendToViewer(id, new { type = "error", message = "hdr-unavailable" });
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
        finally
        {
            foreach (var entry in encoders.Values)
            {
                entry.Encoder.Dispose();
                entry.Converter.Dispose();
            }
        }
    }

    internal static (int Width, int Height) FrameSize(Bitmap bitmap)
    {
        float scale = Math.Min(1f, (float)PreviewEncodingFormat.MaxDimension / Math.Max(bitmap.Width, bitmap.Height));
        return (Math.Max(2, (int)(bitmap.Width * scale) / 2 * 2), Math.Max(2, (int)(bitmap.Height * scale) / 2 * 2));
    }

    // One profile's conversion buffers, reused until the frame size changes. The encoder copies the
    // pixels into shared memory before it returns, so the next frame can overwrite them.
    internal sealed class FrameConverter : IDisposable
    {
        private readonly byte[] _pixels;
        private readonly SKSurface? _surface;
        private readonly Bitmap? _resized;

        internal FrameConverter(int width, int height, bool hdr)
        {
            Width = width;
            Height = height;
            _pixels = GC.AllocateArray<byte>(PreviewEncodingFormat.BufferBytes(width, height, hdr, audio: false), pinned: true);
            if (hdr)
            {
                _resized = new Bitmap(width, height, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
            }
            else
            {
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                _surface = SKSurface.Create(info, Marshal.UnsafeAddrOfPinnedArrayElement(_pixels, 0), info.RowBytes);
            }
        }

        internal int Width { get; }

        internal int Height { get; }

        internal byte[] Convert(Bitmap bitmap, UIToneMappingOperator toneMapping, float exposure)
        {
            // Both targets are raster canvases, which read the source during the draw call, so the
            // image wraps its pixels instead of the copy SKImage.FromBitmap makes of a mutable bitmap.
            using var pixmap = bitmap.SKBitmap.PeekPixels();
            using var image = pixmap is not null ? SKImage.FromPixels(pixmap) : SKImage.FromBitmap(bitmap.SKBitmap);
            if (_resized is { } resized)
            {
                // Resize in floating-point linear light. Converting to PQ only after resizing preserves
                // values above SDR white; Skia’s PQ transfer already uses 203 nit reference white.
                using (var canvas = new SKCanvas(resized.SKBitmap))
                {
                    canvas.Clear(SKColors.Black);
                    canvas.DrawImage(image, SKRect.Create(Width, Height), new SKSamplingOptions(SKFilterMode.Linear));
                }
                var pq = BitmapColorSpaceMapping.BuildTargetColorSpace(BitmapColorTransfer.Pq, BitmapColorPrimaries.Rec2020);
                using var converted = resized.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, pq);
                converted.GetPixelSpan().CopyTo(_pixels);
                return _pixels;
            }
            SKCanvas target = _surface!.Canvas;
            target.Clear(SKColors.Black);
            PreviewFrameRenderer.Draw(target, image, SKRect.Create(bitmap.Width, bitmap.Height),
                SKRect.Create(Width, Height), BitmapInterpolationMode.HighQuality,
                toneMapping, exposure);
            return _pixels;
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _resized?.Dispose();
        }
    }

    internal sealed record PendingFrame(Ref<Bitmap> Image, UIToneMappingOperator ToneMapping, float Exposure,
        bool Playing, double Time, long PlaybackId, double Clock) : IDisposable
    {
        internal PendingFrame Snapshot() => this with { Image = Image.Clone() };
        public void Dispose() => Image.Dispose();
    }
}
