using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using static NAudio.Wave.MediaFoundationReader;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;

using Beutl.Embedding.MediaFoundation;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
using Beutl.Extensions.MediaFoundation;
#endif

public class MFReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<MFReader>();

    private readonly MFDecoder? _decoder;
    private readonly VideoStreamInfo? _videoInfo;
    private readonly BitmapColorSpace _videoColorSpace;
    private readonly PixelFormatConverter.InvYuvMatrix8 _yuy2Inverse;

    private readonly AudioStreamInfo? _audioInfo;
    private readonly MediaFoundationReader? _audioReader;
    private readonly WaveFormat? _waveFormat;
    private readonly ISampleProvider? _provider;

    public MFReader(string file, MediaOptions options, MFDecodingExtension extension)
    {
        _videoColorSpace = BitmapColorSpace.Srgb;
        bool wantsAudio = options.StreamsToLoad.HasFlag(MediaMode.Audio);
        try
        {
            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                // Wrap decoder creation so a broken video track does not also
                // hide a readable audio track from the caller. When the caller
                // asked only for video, let the exception propagate.
                try
                {
                    _decoder = new MFDecoder(file, new MediaOptions(MediaMode.Video), extension);
                }
                catch (Exception ex) when (wantsAudio)
                {
                    _logger.LogWarning(ex,
                        "Video stream could not be initialized; opening as audio-only because StreamsToLoad includes Audio");
                }
            }

            if (_decoder != null)
            {
                MFMediaInfo info = _decoder.GetMediaInfo();
                _videoInfo = new VideoStreamInfo(
                    info.VideoFormatName ?? "Unknown",
                    info.TotalFrameCount,
                    new PixelSize(info.ImageFormat.Width, info.ImageFormat.Height),
                    new Rational(info.Fps.Numerator, info.Fps.Denominator));
                // Resolve the target color space exactly once. HDR inputs are mapped
                // by ForceSrgbGamma, which has two modes:
                //   • false (default): build the HDR-tagged color space (PQ/HLG +
                //     luminance-scaled Rec.2020 gamut). Skia applies the proper EOTF
                //     in preview, producing the intended HDR look on the editor —
                //     but the YUY2 8-bit decode path already quantized the samples,
                //     so the result will show some banding compared to a true HDR
                //     viewing pipeline.
                //   • true: clip to plain sRGB instead. Recommended when the editor
                //     monitor is SDR; avoids the HDR EOTF being applied to 8-bit
                //     samples in a viewer that cannot reproduce the result anyway.
                if (info.IsHdr)
                {
                    _videoColorSpace = extension.Settings.ForceSrgbGamma
                        ? BitmapColorSpace.Srgb
                        : MFColorSpaceHelper.BuildHdrColorSpace(info.TransferFunction, info.ColorPrimaries);
                }
                else
                {
                    _videoColorSpace = MFColorSpaceHelper.BuildTargetColorSpace(info.TransferFunction, info.ColorPrimaries);
                    // Surface the case where the transfer tag was present but didn't
                    // map onto any known TRC — the helper silently falls back to
                    // sRGB and that may look wrong for forward-looking transfers.
                    if (info.TransferFunction != Vortice.MediaFoundation.VideoTransferFunction.FuncUnknown
                        && !MFColorSpaceHelper.TryGetTransferFunction(info.TransferFunction, out _))
                    {
                        _logger.LogWarning(
                            "Stream transfer function {Trc} is not mapped; defaulting to sRGB. Colors may look off.",
                            info.TransferFunction);
                    }
                }
                // Pick the inverse YUV matrix that matches the stream tag so the
                // YUY2 → BGRA conversion below produces the right RGB values
                // before Skia interprets _videoColorSpace. Falling back to
                // BT.709 mirrors what most modern decoders pick for tag-less
                // HD-or-larger SDR content.
                _yuy2Inverse = info.YCbCrMatrix switch
                {
                    Vortice.MediaFoundation.VideoTransferMatrix.Bt601 => PixelFormatConverter.InvYuvMatrix8.Bt601,
                    Vortice.MediaFoundation.VideoTransferMatrix.Bt202010 => PixelFormatConverter.InvYuvMatrix8.Bt2020,
                    Vortice.MediaFoundation.VideoTransferMatrix.Smpte240m => PixelFormatConverter.InvYuvMatrix8.Smpte240M,
                    _ => PixelFormatConverter.InvYuvMatrix8.Bt709,
                };
                HasVideo = true;
            }

            if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                _audioReader = new MediaFoundationReader(file, new MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true
                });
                _waveFormat = _audioReader.WaveFormat;

                _provider = _audioReader.ToSampleProvider().ToStereo();

                _audioInfo = new AudioStreamInfo(
                    CodecName: _waveFormat.Encoding.ToString(),
                    Duration: new Rational(_audioReader.Length, _waveFormat.AverageBytesPerSecond),
                    SampleRate: _waveFormat.SampleRate,
                    NumChannels: _waveFormat.Channels);
                HasAudio = true;
            }
        }
        finally
        {
        }
    }

    public override VideoStreamInfo VideoInfo => _videoInfo ?? throw new NotSupportedException();

    public override AudioStreamInfo AudioInfo => _audioInfo ?? throw new NotSupportedException();

    public override bool HasVideo { get; }

    public override bool HasAudio { get; }

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadVideoCore(frame, out image);
        }
        else
        {
            image = null;
            if (!HasVideo || _decoder == null || IsDisposed)
                return false;

            (bool result, Ref<Bitmap>? image1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadVideoCore(frame, out Ref<Bitmap>? image1);
                return (ret, image1);
            });
            image = image1!;
            return result;
        }
    }

    private unsafe bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        if (!HasVideo || _decoder == null || IsDisposed)
            return false;

        MFMediaInfo info = _decoder.GetMediaInfo();
        int w = info.ImageFormat.Width;
        int h = info.ImageFormat.Height;

        // YUY2: 2 bytes per pixel
        int yuy2Size = w * h * 2;
        byte[] yuy2Buffer = ArrayPool<byte>.Shared.Rent(yuy2Size);
        try
        {
            int r;
            fixed (byte* ptr = yuy2Buffer)
            {
                r = _decoder.ReadFrame(frame, (nint)ptr);
            }

            if (r != 0)
            {
                // Tag the Bitmap with the source's color space (not sRGB) so Skia
                // applies the right transfer function on sampling. For HDR input
                // this is PQ/HLG + Rec.2020 with luminance scaling already baked in.
                var result = new Bitmap(w, h,
                    BitmapColorType.Bgra8888, BitmapAlphaType.Premul, _videoColorSpace);
                fixed (byte* srcPtr = yuy2Buffer)
                {
                    // YuvConversion.Yuy2ToBgra is BT.601-only; use the
                    // matrix-aware variant so BT.709 / BT.2020 / SMPTE 240M
                    // sources are not silently miscolored.
                    PixelFormatConverter.Yuy2ToBgra(
                        srcPtr, (byte*)result.Data, result.RowBytes, w, h, _yuy2Inverse);
                }

                image = Ref<Bitmap>.Create(result);
                return true;
            }
            else
            {
                return false;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(yuy2Buffer);
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadAudioCore(start, length, out sound);
        }
        else
        {
            sound = null;
            if (IsDisposed || _audioReader == null || _waveFormat == null || _provider == null)
                return false;

            (bool result, Ref<IPcm>? sound1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadAudioCore(start, length, out Ref<IPcm>? sound1);
                return (ret, sound1);
            });
            sound = sound1!;
            return result;
        }
    }

    private bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        if (IsDisposed || _audioReader == null || _waveFormat == null || _provider == null)
            return false;

        _audioReader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);
        var tmp = new Pcm<Stereo32BitFloat>(_waveFormat.SampleRate, (int)(length / (double)_waveFormat.SampleRate * _waveFormat.SampleRate));

        float[] buffer = new float[tmp.NumSamples * 2];
        int count = _provider.Read(buffer, 0, buffer.Length);
        if (count >= 0)
        {
            buffer.CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(tmp.DataSpan));

            sound = Ref<IPcm>.Create(tmp);
            return true;
        }
        else
        {
            tmp.Dispose();
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _decoder?.Dispose();
        _audioReader?.Dispose();
    }
}
