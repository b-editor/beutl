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
    // Counts published frames, so coding a paused frame again can be told apart from coding a new one.
    private long _published;
    private bool _hdrUnavailable;
    private int _keyFrame = 1;

    private void RequestKeyFrame()
    {
        Interlocked.Exchange(ref _keyFrame, 1);
        _updates.Writer.TryWrite(true);
    }

    // Called with _gate held. Every viewer shares one encoder per dynamic range, so while any viewer can be reached
    // only through a TURN relay, all of them get the relayed stream.
    private bool IsRelayed() => _peers.Values.Any(viewer => viewer.Relayed);

    // Called with _gate held; null removes the viewer.
    private void SetViewer(string id, Viewer? viewer)
    {
        bool relayed = IsRelayed();
        if (viewer is { } value) _peers[id] = value;
        else _peers.Remove(id);
        // A paused frame is sent again at the direct quality once the last relayed viewer has gone.
        if (relayed && !IsRelayed()) _updates.Writer.TryWrite(true);
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
            _published++;
            _updates.Writer.TryWrite(true);
        }
    }

    private async Task EncodeFramesAsync()
    {
        var encoders = new Dictionary<bool, (FrameConverter Converter, PreviewEncoder Encoder)>();
        var still = new StillRefinement();
        bool relayed = true;
        var cadence = Stopwatch.StartNew();
        try
        {
            while (await _updates.Reader.WaitToReadAsync(_stop.Token))
            {
                var remaining = TimeSpan.FromSeconds(1d / PreviewEncodingFormat.FrameRate(relayed)) - cadence.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _stop.Token);
                PendingFrame? frame;
                long published;
                bool[] profiles;
                lock (_gate)
                {
                    _updates.Reader.TryRead(out _);
                    frame = _latest?.Snapshot();
                    published = _published;
                    profiles = _peers.Values.Select(viewer => viewer.Hdr).Distinct().Where(hdr => !hdr || !_hdrUnavailable).ToArray();
                    relayed = IsRelayed();
                }
                if (frame is null) continue;
                // Pace from the start of each frame, so the time spent converting and encoding it does not lower the rate.
                cadence.Restart();
                using (frame)
                {
                    // A keyframe requested while paused is a new RTP frame, even with unchanged pixels.
                    uint timestamp = (uint)(_mediaClock.Elapsed.TotalSeconds * 90000);
                    bool keyFrame = Interlocked.Exchange(ref _keyFrame, 0) != 0;
                    bool keyFrameCoded = false;
                    var (width, height) = FrameSize(frame.Image.Value, relayed);
                    foreach (bool hdr in profiles)
                    {
                        try
                        {
                            bool reset = !encoders.TryGetValue(hdr, out var entry) || entry.Encoder.Relayed != relayed
                                || entry.Converter.Width != width || entry.Converter.Height != height;
                            if (reset)
                            {
                                entry.Encoder?.Dispose();
                                entry.Converter?.Dispose();
                                var converter = new FrameConverter(width, height, hdr);
                                try
                                {
                                    entry = (converter, new PreviewEncoder(width, height, hdr, relayed: relayed));
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
                            keyFrameCoded |= IsKeyFrame(data, hdr);
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
                                foreach (string id in _peers.Where(p => p.Value.Hdr).Select(p => p.Key))
                                    SendToViewer(id, new { type = "error", message = "hdr-unavailable" });
                            }
                        }
                    }
                    if (profiles.Length != 0 && still.Coded(published, frame.Playing, keyFrameCoded))
                        _updates.Writer.TryWrite(true);
                }
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

    internal static (int Width, int Height) FrameSize(Bitmap bitmap, bool relayed)
    {
        float scale = Math.Min(1f, (float)PreviewEncodingFormat.MaxDimension(relayed) / Math.Max(bitmap.Width, bitmap.Height));
        return (Math.Max(2, (int)(bitmap.Width * scale) / 2 * 2), Math.Max(2, (int)(bitmap.Height * scale) / 2 * 2));
    }

    // One profile's conversion buffers, reused until the frame size changes. The encoder copies the
    // pixels into shared memory before it returns, so the next frame can overwrite them.
    internal sealed class FrameConverter : IDisposable
    {
        // Skia rasterizes on the calling thread, so the SDR frame is drawn as horizontal bands on several.
        private static readonly int s_bands = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        private readonly byte[] _pixels;
        private readonly SKSurface[] _bands = [];
        private readonly Bitmap? _resized;

        // bands overrides the per-machine band count, so tests cover band edges on any processor count.
        internal FrameConverter(int width, int height, bool hdr, int bands = 0)
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
                nint address = Marshal.UnsafeAddrOfPinnedArrayElement(_pixels, 0);
                int rowBytes = width * PreviewEncodingFormat.BytesPerPixel(hdr: false);
                // At least 16 rows per band keeps every band non-empty after rounding its top down to 8 rows.
                int count = Math.Clamp(bands > 0 ? bands : s_bands, 1, Math.Max(1, height / 16));
                _bands = new SKSurface[count];
                for (int band = 0; band < count; band++)
                {
                    // Bands start on a multiple of 8 rows, where Skia's dither pattern repeats, so the
                    // pixels match a frame drawn in one piece.
                    int top = band * height / count / 8 * 8;
                    int bottom = band == count - 1 ? height : (band + 1) * height / count / 8 * 8;
                    var info = new SKImageInfo(width, bottom - top, SKColorType.Bgra8888, SKAlphaType.Premul);
                    _bands[band] = SKSurface.Create(info, address + top * rowBytes, rowBytes);
                    _bands[band].Canvas.Translate(0, -top);
                }
            }
        }

        internal int Width { get; }

        internal int Height { get; }

        internal byte[] Convert(Bitmap bitmap, UIToneMappingOperator toneMapping, float exposure)
        {
            using var image = bitmap.CreateImageWithoutCopy();
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
            // A frame already at the encoded size needs no resampling, which would only soften it.
            var interpolation = bitmap.Width == Width && bitmap.Height == Height
                ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
            Parallel.ForEach(_bands, surface =>
            {
                surface.Canvas.Clear(SKColors.Black);
                PreviewFrameRenderer.Draw(surface.Canvas, image, SKRect.Create(bitmap.Width, bitmap.Height),
                    SKRect.Create(Width, Height), interpolation, toneMapping, exposure);
            });
            return _pixels;
        }

        public void Dispose()
        {
            foreach (var band in _bands) band.Dispose();
            _resized?.Dispose();
        }
    }

    // VP8 clears the frame tag's lowest bit on a key frame (RFC 6386). In VP9 profile 2's first byte,
    // a key frame sets neither show_existing_frame nor frame_type, as the packetizer also reads.
    internal static bool IsKeyFrame(ReadOnlySpan<byte> data, bool hdr)
        => data.Length != 0 && (data[0] & (hdr ? 0x0c : 0x01)) == 0;

    // libvpx adds detail to an unchanged picture with every frame it codes, while a key frame starts again
    // from one frame's budget. So a paused frame is coded until Count frames have followed its last key frame,
    // and at most three times as often in all, since a lost connection can keep asking for key frames.
    internal sealed class StillRefinement
    {
        // Where natural footage stopped improving at both qualities: 36.6 to 43.3 dB at 1080p and 8 Mbps,
        // and 35.5 to 43.4 dB at 720p and 2 Mbps, after about 0.9 MB and 0.4 MB. The encoder's key frame
        // interval is at least twice this, so one paused frame meets at most one scheduled key frame.
        internal const int Count = 30;
        private long _frame = -1;
        private int _sinceKeyFrame;
        private int _coded;

        // Called after a frame is coded; returns whether to code the same paused frame again.
        internal bool Coded(long frame, bool playing, bool keyFrame)
        {
            if (playing || frame != _frame) _sinceKeyFrame = _coded = 0;
            _frame = frame;
            if (playing) return false;
            _coded++;
            _sinceKeyFrame = keyFrame ? 1 : _sinceKeyFrame + 1;
            return _sinceKeyFrame < Count && _coded < Count * 3;
        }
    }

    internal sealed record PendingFrame(Ref<Bitmap> Image, UIToneMappingOperator ToneMapping, float Exposure,
        bool Playing, double Time, long PlaybackId, double Clock) : IDisposable
    {
        internal PendingFrame Snapshot() => this with { Image = Image.Clone() };
        public void Dispose() => Image.Dispose();
    }
}
