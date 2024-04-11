using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using SharpDX;
using SharpDX.Direct3D9;
using SharpDX.MediaFoundation;
using SharpDX.Multimedia;
using SharpDX.Win32;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

// MediaFoundationを使用して、Bitmapから動画を作成するクラス
public unsafe class MFWriter : MediaWriter
{
    private SinkWriter _sinkWriter;
    private int _videoStreamIndex;

    public MFWriter(string file, MFVideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
        : base(videoConfig, audioConfig)
    {
        // sinkwriterを初期化
        _sinkWriter = MediaFactory.CreateSinkWriterFromURL(file, null, null);
        _videoStreamIndex = ConfigureVideoEncoder(videoConfig);

        _sinkWriter.BeginWriting();
    }

    // IMFMediaTypeを作成
    private static MediaType CreateMediaTypeFromSubtype(Guid subtype, int width, int height, double rate)
    {
        var mediaType = new MediaType();
        mediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        mediaType.Set(MediaTypeAttributeKeys.Subtype, subtype);
        mediaType.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);
        mediaType.Set(MediaTypeAttributeKeys.FrameSize, ((long)width << 32) | (uint)height);
        mediaType.Set(MediaTypeAttributeKeys.FrameRate, ((long)(int)(rate * 10000000) << 32 | 10000000));
        return mediaType;
    }

    // ConfigureVideoEncoder
    private int ConfigureVideoEncoder(MFVideoEncoderSettings videoConfig)
    {
        using var outputType = CreateMediaTypeFromSubtype(
            videoConfig.Format.ToVideoFormatGuid(),
            videoConfig.DestinationSize.Width,
            videoConfig.DestinationSize.Height,
            videoConfig.FrameRate.ToDouble());
        outputType.Set(MediaTypeAttributeKeys.AvgBitrate, videoConfig.Bitrate);
        _sinkWriter.AddStream(outputType, out int streamIndex);

        // InputType
        using var inputType = CreateMediaTypeFromSubtype(
            VideoFormatGuids.Argb32,
            videoConfig.SourceSize.Width,
            videoConfig.SourceSize.Height,
            videoConfig.FrameRate.ToDouble());
        _sinkWriter.SetInputMediaType(streamIndex, inputType, null);

        return streamIndex;
    }

    public override long NumberOfFrames { get; }

    public override long NumberOfSamples { get; }

    public override bool AddVideo(IBitmap image)
    {
        bool requireDispose = false;

        if (image is not Bitmap<Bgra8888>)
        {
            image = image.Convert<Bgra8888>();
            requireDispose = true;
        }

        try
        {
            using var buffer = MediaFactory.CreateMemoryBuffer(image.ByteCount);
            IntPtr ptr = buffer.Lock(out _, out _);
            Buffer.MemoryCopy((void*)image.Data, (void*)ptr, image.ByteCount, image.ByteCount);
            buffer.Unlock();
            buffer.CurrentLength = image.ByteCount;

            using var sample = MediaFactory.CreateSample();
            sample.AddBuffer(buffer);

            _sinkWriter.WriteSample(_videoStreamIndex, sample);

            return true;
        }
        finally
        {
            if (requireDispose)
                image.Dispose();
        }
    }

    public override bool AddAudio(IPcm sound)
    {
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _sinkWriter.Finalize();
        _sinkWriter.Dispose();
    }
}
