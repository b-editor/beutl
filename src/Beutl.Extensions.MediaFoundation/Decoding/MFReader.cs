using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Rendering;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using SharpDX.MediaFoundation;

using static NAudio.Wave.MediaFoundationReader;

using MediaType = SharpDX.MediaFoundation.MediaType;
using Sample = SharpDX.MediaFoundation.Sample;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class MFReader : MediaReader
{
    private static readonly Lazy<(Guid, string name)[]> s_videoFormats = new(() => typeof(VideoFormatGuids)
        .GetFields()
        .Select(f => f.GetValue(null) is Guid id ? ((Guid?)id, f) : (null, f))
        .Where(v => v.Item1.HasValue)
        .Select(v => (v.Item1!.Value, v.f.Name))
        .ToArray());

    private readonly ILogger _logger = Log.CreateLogger<MFReader>();
    private readonly string _file;
    private readonly MediaOptions _options;
    private readonly SourceReader? _sourceReader;
    private readonly MediaAttributes? _attributes;
    private readonly MediaType? _newMediaType;
    private long _videoNowFrame;
    private readonly VideoStreamInfo? _videoInfo;

    private readonly AudioStreamInfo? _audioInfo;
    private readonly NAudio.Wave.MediaFoundationReader? _audioReader;
    private readonly WaveFormat? _waveFormat;
    private readonly MediaFoundationResampler? _resampler;

    public MFReader(string file, MediaOptions options)
    {
        _file = file;
        _options = options;
        try
        {
            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                try
                {
                    _attributes = new MediaAttributes(1);
                    _newMediaType = new MediaType();

                    //SourceReaderに動画のパスを設定
                    _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing.Guid, true);
                    _sourceReader = new SourceReader(file, _attributes);

                    MediaType originalmediaType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    Guid subtype = originalmediaType.Get(MediaTypeAttributeKeys.Subtype);
                    (Guid, string name) fmt = s_videoFormats.Value
                        .FirstOrDefault(v => v.Item1 == subtype);

                    _newMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    _newMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                    _sourceReader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, _newMediaType);

                    MediaType mediaType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    // 100ナノ秒
                    long duration = _sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
                    long framerate = mediaType.Get(MediaTypeAttributeKeys.FrameRate);
                    long frameSize = mediaType.Get(MediaTypeAttributeKeys.FrameSize);
                    var size = new PixelSize((int)(frameSize >> 32), (int)(frameSize & 0xffffffff));
                    var rate = new Rational((int)(framerate >> 32), (int)(framerate & 0xffffffff));

                    _videoInfo = new VideoStreamInfo(
                        fmt.name ?? subtype.ToString(),
                        new Rational(duration, 1000 * 1000 * 10),
                        size,
                        rate);
                    HasVideo = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during initialization of the video stream.");
                }
            }

            if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                try
                {
                    _audioReader = new NAudio.Wave.MediaFoundationReader(_file, new MediaFoundationReaderSettings
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during initialization of the audio stream.");

                }
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

    private unsafe Bitmap<Bgra8888> SampleToBitmap(Sample sample)
    {
        using (MediaBuffer buf = sample.ConvertToContiguousBuffer())
        {
            try
            {
                nint pBuffer = buf.Lock(out int maxLength, out int currentLength);

                var bmp = new Bitmap<Bgra8888>(VideoInfo.FrameSize.Width, VideoInfo.FrameSize.Height);
                Buffer.MemoryCopy((void*)pBuffer, (void*)bmp.Data, bmp.ByteCount, bmp.ByteCount);

                return bmp;
            }
            finally
            {
                buf.Unlock();
            }
        }
    }

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadVideoCore(frame, out image);
        }
        else
        {
            image = null;
            if (!HasVideo || _sourceReader == null || IsDisposed)
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

    const long SEEK_TOLERANCE = 10000000;
    const long MAX_FRAMES_TO_SKIP = 10;

    private unsafe bool ReadVideoCore(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (!HasVideo || _sourceReader == null || IsDisposed)
        {
            image = null;
            return false;
        }

        long hns = (long)((frame / VideoInfo.FrameRate.ToDouble()) * 1000 * 1000 * 10);
        long skip = frame - _videoNowFrame;
        if (skip > 100 || skip < 0)
        {
            _sourceReader.SetCurrentPosition(hns);
        }

        Sample? sample = null;
        int cSkipped = 0;
        while (true)
        {
            Sample? sampleTmp = _sourceReader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlags.None,
                out _,
                out SourceReaderFlags readerFlags,
                out _);

            if (sampleTmp == null || readerFlags.HasFlag(SourceReaderFlags.Error))
            {
                continue;
            }

            if (readerFlags.HasFlag(SourceReaderFlags.Endofstream))
            {
                break;
            }

            if (readerFlags.HasFlag(SourceReaderFlags.Currentmediatypechanged))
            {
                image = null;
                return false;
            }

            // We got a sample. Hold onto it.

            sample?.Dispose();
            sample = sampleTmp;

            long timeStamp = sample.SampleTime;
            // Keep going until we get a frame that is within tolerance of the
            // desired seek position, or until we skip MAX_FRAMES_TO_SKIP frames.

            // During this process, we might reach the end of the file, so we
            // always cache the last sample that we got (pSample).

            if ((cSkipped < MAX_FRAMES_TO_SKIP) &&
                 (timeStamp + SEEK_TOLERANCE < hns))
            {
                sampleTmp.Dispose();

                ++cSkipped;
                continue;
            }

            hns = timeStamp;
            break;
        }

        if (sample?.IsDisposed == false)
        {
            image = SampleToBitmap(sample);
            sample.Dispose();
            _videoNowFrame = frame;
            return true;
        }

        image = null;
        return false;
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
        _sourceReader?.Dispose();
        _attributes?.Dispose();
        _newMediaType?.Dispose();
        _audioReader?.Dispose();
        _resampler?.Dispose();
    }
}
