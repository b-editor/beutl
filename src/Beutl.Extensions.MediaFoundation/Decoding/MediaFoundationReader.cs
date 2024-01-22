using System.Diagnostics.CodeAnalysis;

using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Pixel;

using SharpDX.MediaFoundation;

using MediaType = SharpDX.MediaFoundation.MediaType;
using Sample = SharpDX.MediaFoundation.Sample;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class MediaFoundationReader : MediaReader
{
    private string _file;
    private MediaOptions _options;
    private SourceReader _sourceReader;
    private MediaAttributes _attributes;
    private MediaType _newMediaType;
    private MediaType _newAudioMediaType;
    private long _videoNowFrame;
    private readonly VideoStreamInfo? _videoInfo;
    private readonly AudioStreamInfo? _audioInfo;

    public MediaFoundationReader(string file, MediaOptions options)
    {
        _file = file;
        _options = options;

        try
        {
            _attributes = new MediaAttributes(1);
            _newMediaType = new MediaType();
            _newAudioMediaType = new MediaType();

            //SourceReaderに動画のパスを設定
            _attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing.Guid, true);
            _sourceReader = new SourceReader(file, _attributes);

            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                try
                {
                    MediaType originalmediaType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    var subtype = originalmediaType.Get(MediaTypeAttributeKeys.Subtype);
                    var t = typeof(VideoFormatGuids)
                        .GetFields()
                        .Select(f => f.GetValue(null) is Guid id ? ((Guid?)id, f) : (null, f))
                        .FirstOrDefault(v => v.Item1 == subtype);

                    _newMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    //出力メディアタイプをRGB32bitに設定
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
                        t.f?.Name ?? subtype.ToString(),
                        new Rational(duration, 1000 * 10),
                        size,
                        rate);
                    HasVideo = true;
                }
                catch
                {
                }
            }

            if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                try
                {
                    MediaType originalmediaType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream);
                    var subtype = originalmediaType.Get(MediaTypeAttributeKeys.Subtype);
                    var t = typeof(AudioFormatGuids)
                        .GetFields()
                        .Select(f => f.GetValue(null) is Guid id ? ((Guid?)id, f) : (null, f))
                        .FirstOrDefault(v => v.Item1 == subtype);

                    _newAudioMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    _newAudioMediaType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                    _sourceReader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, _newAudioMediaType);

                    MediaType mediaType = _sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream);
                    long duration = _sourceReader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
                    long sampleRate = mediaType.Get(MediaTypeAttributeKeys.AudioSamplesPerSecond);
                    long numChannels = mediaType.Get(MediaTypeAttributeKeys.AudioNumChannels);

                    _audioInfo = new AudioStreamInfo(
                        t.f?.Name ?? subtype.ToString(),
                        new Rational(duration, 1000 * 10),
                        (int)sampleRate,
                        (int)numChannels);
                    HasAudio = true;
                }
                catch
                {
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
        if (!HasVideo)
        {
            image = null;
            return false;
        }

        long handrednano = (long)(frame * (VideoInfo.FrameRate * 1000 * 10).ToDouble());
        long skip = frame - _videoNowFrame;
        if (skip > 100 || skip < 0)
        {
            _sourceReader.SetCurrentPosition(handrednano);
        }

        Sample prevSample = _sourceReader.ReadSample(
            SourceReaderIndex.FirstVideoStream,
            SourceReaderControlFlags.None,
            out _,
            out SourceReaderFlags readerFlags,
            out long llTimestampRef);

        if (readerFlags.HasFlag(SourceReaderFlags.Endofstream) || readerFlags.HasFlag(SourceReaderFlags.Error))
        {
            image = null;
            return false;
        }

        if (handrednano <= llTimestampRef)
        {
            image = SampleToBitmap(prevSample);
            prevSample.Dispose();
            _videoNowFrame = frame;
            return true;
        }

        while (true)
        {
            Sample sample = _sourceReader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlags.None,
                out _,
                out SourceReaderFlags readerFlags2,
                out long llTimestampRef2);

            if (sample == null
                || readerFlags2.HasFlag(SourceReaderFlags.Endofstream)
                || readerFlags2.HasFlag(SourceReaderFlags.Error))
            {
                prevSample.Dispose();
                sample?.Dispose();
                break;
            }

            if (llTimestampRef <= handrednano && handrednano <= llTimestampRef2)
            {
                image = SampleToBitmap(sample);
                prevSample.Dispose();
                sample.Dispose();
                _videoNowFrame = frame;
                return true;
            }

            prevSample.Dispose();
            prevSample = sample;
            llTimestampRef = llTimestampRef2;
        }

        image = null;
        return false;
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (!HasAudio)
        {
            sound = null;
            return false;
        }

        long handrednano = (long)((start / (double)AudioInfo.SampleRate) * 1000 * 10);
        _sourceReader.SetCurrentPosition(handrednano);

        sound = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _sourceReader.Dispose();
        _attributes.Dispose();
        _newMediaType.Dispose();
        _newAudioMediaType.Dispose();
    }
}
