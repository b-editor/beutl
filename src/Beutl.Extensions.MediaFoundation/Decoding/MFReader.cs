using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

using NAudio.Wave;

using OpenCvSharp;

using static NAudio.Wave.MediaFoundationReader;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class MFReader : MediaReader
{
    private readonly string _file;
    private readonly MediaOptions _options;

    private readonly MFDecoder? _decoder;
    private readonly VideoStreamInfo? _videoInfo;

    private readonly AudioStreamInfo? _audioInfo;
    private readonly MediaFoundationReader? _audioReader;
    private readonly WaveFormat? _waveFormat;
    private readonly MediaFoundationResampler? _resampler;

    public MFReader(string file, MediaOptions options, MFDecodingExtension extension)
    {
        _file = file;
        _options = options;
        try
        {
            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                _decoder = new MFDecoder(file, new MediaOptions(MediaMode.Video), extension);
                MFMediaInfo info = _decoder.GetMediaInfo();
                _videoInfo = new VideoStreamInfo(
                    info.VideoFormatName ?? "Unknown",
                    info.TotalFrameCount,
                    new PixelSize(info.ImageFormat.Width, info.ImageFormat.Height),
                    new Rational(info.Fps.Numerator, info.Fps.Denominator));
                HasVideo = true;
            }

            if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                _audioReader = new MediaFoundationReader(_file, new MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true
                });
                _waveFormat = _audioReader.WaveFormat;

                _resampler = new MediaFoundationResampler(_audioReader, WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, 2));

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

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
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

            (bool result, IBitmap? image1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadVideoCore(frame, out IBitmap? image1);
                return (ret, image1);
            });
            image = image1!;
            return result;
        }
    }

    private unsafe bool ReadVideoCore(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        image = null;
        if (!HasVideo || _decoder == null || IsDisposed)
            return false;

        MFMediaInfo info = _decoder.GetMediaInfo();
        using var mat = new Mat(info.ImageFormat.Height, info.ImageFormat.Width, MatType.CV_8UC2);

        int r = _decoder.ReadFrame(frame, mat.Data);
        if (r != 0)
        {
            using var dst = new Mat(info.ImageFormat.Height, info.ImageFormat.Width, MatType.CV_8UC4);
            Cv2.CvtColor(mat, dst, ColorConversionCodes.YUV2BGRA_YUY2);
            var result = new Bitmap<Bgra8888>(info.ImageFormat.Width, info.ImageFormat.Height);
            Buffer.MemoryCopy((void*)dst.Data, (void*)result.Data, result.ByteCount, result.ByteCount);

            image = result;
            return true;
        }
        else
        {
            return false;
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadAudioCore(start, length, out sound);
        }
        else
        {
            sound = null;
            if (IsDisposed || _audioReader == null || _waveFormat == null || _resampler == null)
                return false;

            (bool result, IPcm? sound1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadAudioCore(start, length, out IPcm? sound1);
                return (ret, sound1);
            });
            sound = sound1!;
            return result;
        }
    }

    private bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        sound = null;
        if (IsDisposed || _audioReader == null || _waveFormat == null || _resampler == null)
            return false;

        _audioReader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);
        var tmp = new Pcm<Stereo32BitFloat>(_options.SampleRate, (int)(length / (double)_waveFormat.SampleRate * _options.SampleRate));

        byte[] buffer = new byte[tmp.NumSamples * 2 * sizeof(float)];
        int count = _resampler.Read(buffer, 0, buffer.Length);
        if (count >= 0)
        {
            buffer.CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, byte>(tmp.DataSpan));

            sound = tmp;
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
        _resampler?.Dispose();
    }
}
