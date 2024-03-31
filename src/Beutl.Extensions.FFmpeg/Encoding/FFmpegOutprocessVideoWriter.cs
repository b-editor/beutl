using System.IO.Pipes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Pixel;

using FFmpeg.AutoGen;

using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed unsafe class FFmpegOutprocessVideoWriter : IFFmpegVideoWriter
{
    private readonly FFmpegEncodingSettings _settings;
    private readonly FFOptions _ffOptions;

    private readonly CustomRawVideoPipeSource _videoFramesSource;
    private readonly Task<bool> _videoTask;
    private readonly Subject<IVideoFrame> _videoFramesSubject;

    private readonly string _formatName;
    private readonly AVOutputFormat* _outputFormat;
    private readonly string _outputFile;
    private PipeStream? _stream;

    public FFmpegOutprocessVideoWriter(string file, FFmpegVideoEncoderSettings videoConfig, FFmpegEncodingSettings settings)
    {
        try
        {
            _settings = settings;
            VideoConfig = videoConfig;
            _outputFile = file;
            _ffOptions = new() { BinaryFolder = Path.GetDirectoryName(FFmpegLoader.GetExecutable())!, UseCache = false };
            GlobalFFOptions.Configure(_ffOptions);


            _outputFormat = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (_outputFormat == null)
                throw new Exception("av_guess_format failed");

            _formatName = UTF8Marshaler.FromNative(System.Text.Encoding.UTF8, _outputFormat->name);

            // 動画ストリーム
            _videoFramesSubject = new Subject<IVideoFrame>();
            _videoFramesSource = new CustomRawVideoPipeSource(_videoFramesSubject.ToEnumerable(), s => _stream = s as PipeStream)
            {
                Width = videoConfig.SourceSize.Width,
                Height = videoConfig.SourceSize.Height,
                StreamFormat = "bgra",
                FrameRate = videoConfig.FrameRate,
            };

            _videoTask = Task.Factory.StartNew(() => FFMpegArguments.FromPipeInput(_videoFramesSource, ConfigureInputArguments)
                .OutputToFile(_outputFile, true, ConfigureVideoOutputArguments)
                .ProcessSynchronously(ffMpegOptions: _ffOptions),
                TaskCreationOptions.LongRunning);

            // 音声ストリームは遅延初期化
        }
        catch
        {
            throw;
        }
    }

    public long NumberOfFrames { get; private set; }

    public FFmpegVideoEncoderSettings VideoConfig { get; }

    private void ConfigureInputArguments(FFMpegArgumentOptions options)
    {
        if (_settings.Acceleration != FFmpegEncodingSettings.AccelerationOptions.Software)
        {
            options.WithHardwareAcceleration(_settings.Acceleration switch
            {
                FFmpegEncodingSettings.AccelerationOptions.D3D11VA => HardwareAccelerationDevice.D3D11VA,
                FFmpegEncodingSettings.AccelerationOptions.DXVA2 => HardwareAccelerationDevice.DXVA2,
                FFmpegEncodingSettings.AccelerationOptions.QSV => HardwareAccelerationDevice.QSV,
                FFmpegEncodingSettings.AccelerationOptions.CUVID => HardwareAccelerationDevice.CUVID,
                FFmpegEncodingSettings.AccelerationOptions.CUDA => HardwareAccelerationDevice.CUDA,
                FFmpegEncodingSettings.AccelerationOptions.VDPAU => HardwareAccelerationDevice.VDPAU,
                FFmpegEncodingSettings.AccelerationOptions.VAAPI => HardwareAccelerationDevice.VAAPI,
                FFmpegEncodingSettings.AccelerationOptions.LibMFX => HardwareAccelerationDevice.LibMFX,
                _ => HardwareAccelerationDevice.Auto,
            });
        }

        if (_settings.Acceleration == FFmpegEncodingSettings.AccelerationOptions.Software
            && _settings.ThreadCount != 1)
        {
            if (_settings.ThreadCount <= 0)
            {
                options.UsingMultithreading(true);
            }
            else
            {
                options.UsingThreads(Math.Clamp(_settings.ThreadCount, 1, Environment.ProcessorCount));
            }
        }
    }

    private void ConfigureVideoOutputArguments(FFMpegArgumentOptions options)
    {
        var videoConfig = VideoConfig;
        var videoCodec = _outputFormat->video_codec;

        AVCodec* codec = ffmpeg.avcodec_find_encoder((AVCodecID)VideoConfig.Codec);
        if (codec != null
            && codec->type == AVMediaType.AVMEDIA_TYPE_VIDEO
            && codec->id != AVCodecID.AV_CODEC_ID_NONE)
        {
            videoCodec = codec->id;
        }

        string? videoPixFmt = null;
        AVPixelFormat pixFmt = VideoConfig.Format;
        if (pixFmt != AVPixelFormat.AV_PIX_FMT_NONE)
        {
            videoPixFmt = ffmpeg.av_get_pix_fmt_name(pixFmt);
        }

        var args = VideoConfig.Arguments;

        string? preset = VideoConfig.Preset;
        int? crf = VideoConfig.Crf;
        string? profile = VideoConfig.Profile;

        var videoCodecName = ffmpeg.avcodec_get_name(videoCodec);

        options
            .WithVideoBitrate(videoConfig.Bitrate)
            .WithCustomArgument($"-r {videoConfig.FrameRate.Numerator}/{videoConfig.FrameRate.Denominator}")
            .WithCustomArgument($"-g {videoConfig.KeyframeRate}")
            .When(videoConfig.SourceSize != videoConfig.DestinationSize, o =>
                o.Resize(videoConfig.DestinationSize.Width, videoConfig.DestinationSize.Height))
            .When(!string.IsNullOrEmpty(videoPixFmt), o =>
                o.ForcePixelFormat(videoPixFmt!))
            .When(!string.IsNullOrEmpty(preset), o => o.WithCustomArgument($"-preset {preset}"))
            .When(crf.HasValue, o => o.WithConstantRateFactor(crf!.Value))
            .ForceFormat(_formatName)
            .WithVideoCodec(videoCodecName)
            .When(!string.IsNullOrWhiteSpace(args), o => o.WithCustomArgument(args!));
    }

    public bool AddVideo(IBitmap image)
    {
        if (image.PixelType != typeof(Bgra8888))
            throw new InvalidOperationException("Unsupported pixel type.");

        _videoFramesSubject.OnNext(
            new CustomVideoFrame((image as Bitmap<Bgra8888>)?.Clone() ?? image.Convert<Bgra8888>()));

        NumberOfFrames++;

        if (_videoTask.IsFaulted || _videoTask.IsCompleted)
        {
            _videoTask.GetAwaiter().GetResult();
            throw new Exception();
        }

        if (OperatingSystem.IsWindows() && _stream != null)
        {
            _stream.WaitForPipeDrain();
        }

        return true;
    }

    public void Dispose()
    {
        CloseVideo();
    }

    private void CloseVideo()
    {
        _videoFramesSubject.OnCompleted();

        _videoTask.GetAwaiter().GetResult();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
