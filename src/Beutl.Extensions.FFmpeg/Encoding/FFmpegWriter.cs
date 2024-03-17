using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
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

public sealed unsafe class FFmpegWriter : MediaWriter
{
    private readonly FFOptions _ffOptions;

    private readonly CustomRawVideoPipeSource _videoFramesSource;
    private readonly Task<bool> _videoTask;
    private readonly Subject<IVideoFrame> _videoFramesSubject;
    private readonly string _videoTmpFile;

    private RawAudioPipeSource? _audioSamplesSource;
    private Task<bool>? _audioTask;
    private Subject<IAudioSample>? _audioSamplesSubject;
    private string? _audioTmpFile;

    private string _formatName;
    private AVOutputFormat* _outputFormat;
    private long _numFrames;
    private long _numSamples;
    private string _outputFile;

    public FFmpegWriter(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
        : base(videoConfig, audioConfig)
    {
        try
        {
            _ffOptions = new() { BinaryFolder = Path.GetDirectoryName(FFmpegLoader.GetExecutable())!, UseCache = false };

            _outputFile = file;

            _outputFormat = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (_outputFormat == null)
                throw new Exception("av_guess_format failed");

            _formatName = UTF8Marshaler.FromNative(System.Text.Encoding.UTF8, _outputFormat->name);

            // 動画ストリーム
            _videoTmpFile = Path.GetTempFileName();
            _videoFramesSubject = new Subject<IVideoFrame>();
            _videoFramesSource = new CustomRawVideoPipeSource(_videoFramesSubject.ToEnumerable())
            {
                Width = videoConfig.SourceSize.Width,
                Height = videoConfig.SourceSize.Height,
                StreamFormat = "bgra",
                FrameRate = videoConfig.FrameRate,
            };

            _videoTask = Task.Factory.StartNew(() => FFMpegArguments.FromPipeInput(_videoFramesSource)
                .OutputToFile(_videoTmpFile, true, ConfigureVideoOutputArguments)
                .ProcessSynchronously(ffMpegOptions: _ffOptions),
                TaskCreationOptions.LongRunning);

            // 音声ストリームは遅延初期化
        }
        catch
        {
            throw;
        }
    }

    public override long NumberOfFrames => _numFrames;

    public override long NumberOfSamples => _numSamples;

    private void ConfigureVideoOutputArguments(FFMpegArgumentOptions options)
    {
        var videoConfig = VideoConfig;
        var videoCodec = _outputFormat->video_codec;
        var videoOptions = videoConfig.CodecOptions as JsonObject;

        if (JsonHelper.TryGetString(videoOptions, "Codec", out string? codecStr))
        {
            AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get_by_name(codecStr);
            if (desc != null
                && desc->type == AVMediaType.AVMEDIA_TYPE_VIDEO
                && desc->id != AVCodecID.AV_CODEC_ID_NONE)
            {
                videoCodec = desc->id;
            }
        }

        string? videoPixFmt = null;
        if (JsonHelper.TryGetString(videoOptions, "Format", out string? fmtStr))
        {
            AVPixelFormat pixFmt = ffmpeg.av_get_pix_fmt(fmtStr);
            if (pixFmt != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                videoPixFmt = ffmpeg.av_get_pix_fmt_name(pixFmt);
            }
        }

        JsonHelper.TryGetString(videoOptions, "Arguments", out string? args);

        string? preset = JsonHelper.TryGetString(videoOptions, "Preset", out string? presetStr) ? presetStr : null;
        int? crf = JsonHelper.TryGetInt(videoOptions, "Crf", out int crf2) ? crf2 : null;
        string? profile = JsonHelper.TryGetString(videoOptions, "Profile", out string? profileStr) ? profileStr : null;

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
            //.WithHardwareAcceleration()
            .UsingMultithreading(true)
            .ForceFormat(_formatName)
            .WithVideoCodec(videoCodecName)
            .When(!string.IsNullOrWhiteSpace(args), o => o.WithCustomArgument(args!));
    }

    public override bool AddVideo(IBitmap image)
    {
        if (image.PixelType != typeof(Bgra8888))
            throw new InvalidOperationException("Unsupported pixel type.");

        _videoFramesSubject.OnNext(
            new CustomVideoFrame((image as Bitmap<Bgra8888>)?.Clone() ?? image.Convert<Bgra8888>(), this));

        _numFrames++;

        if (_videoTask.IsFaulted || _videoTask.IsCompleted)
        {
            return false;
        }

        return true;
    }

    private void ConfigureAudioOutputArguments(FFMpegArgumentOptions options)
    {
        var audioConfig = AudioConfig;
        var audioCodec = _outputFormat->audio_codec;
        var audioOptions = audioConfig.CodecOptions as JsonObject;

        if (JsonHelper.TryGetString(audioOptions, "Codec", out string? codecStr))
        {
            AVCodecDescriptor* desc = ffmpeg.avcodec_descriptor_get_by_name(codecStr);
            if (desc != null
                && desc->type == AVMediaType.AVMEDIA_TYPE_AUDIO
                && desc->id == AVCodecID.AV_CODEC_ID_NONE)
            {
                audioCodec = desc->id;
            }
        }

        JsonHelper.TryGetString(audioOptions, "Arguments", out string? args);

        var audioCodecName = ffmpeg.avcodec_get_name(audioCodec);

        options
            .WithAudioBitrate(audioConfig.Bitrate)
            .WithAudioSamplingRate(AudioConfig.SampleRate)
            .WithCustomArgument($"-ac {AudioConfig.Channels}")
            //.WithHardwareAcceleration()
            .UsingMultithreading(true)
            .ForceFormat(_formatName)
            .WithAudioCodec(audioCodecName)
            .When(!string.IsNullOrWhiteSpace(args), o => o.WithCustomArgument(args!));
    }

    [MemberNotNull(nameof(_audioTmpFile), nameof(_audioSamplesSubject), nameof(_audioSamplesSource), nameof(_audioTask))]
    private void LazyInitializeAudioStream(int sampleRate, int channels, string format)
    {
        _audioTmpFile = Path.GetTempFileName();
        _audioSamplesSubject = new Subject<IAudioSample>();
        _audioSamplesSource = new RawAudioPipeSource(_audioSamplesSubject.ToEnumerable())
        {
            SampleRate = (uint)sampleRate,
            Channels = (uint)channels,
            Format = format
        };

        _audioTask = Task.Factory.StartNew(() => FFMpegArguments.FromPipeInput(_audioSamplesSource)
            .OutputToFile(_audioTmpFile, true, ConfigureAudioOutputArguments)
            .ProcessSynchronously(ffMpegOptions: _ffOptions),
            TaskCreationOptions.LongRunning);
    }

    public override bool AddAudio(IPcm sound)
    {
        if (_audioTmpFile == null
            || _audioSamplesSubject == null
            || _audioSamplesSource == null
            || _audioTask == null)
        {
            LazyInitializeAudioStream(sound.SampleRate, 2, "f32le");
        }

        _audioSamplesSubject.OnNext(new CustomAudioSample(
            (sound as Pcm<Stereo32BitFloat>)?.Clone() ?? sound.Convert<Stereo32BitFloat>(),
            this));

        _numSamples += sound.NumSamples;

        if (_audioTask.IsFaulted || _audioTask.IsCompleted)
        {
            return false;
        }

        return true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        CloseVideo();
        CloseAudio();

        if (disposing)
        {
            if (_audioTmpFile != null)
            {
                Composite();
            }
            else
            {
                File.Move(_videoTmpFile, _outputFile);
            }
        }
    }

    private void Composite()
    {
        FFMpegArguments.FromFileInput(_videoTmpFile)
            .AddFileInput(_audioTmpFile!)
            .OutputToFile(_outputFile, addArguments: o => o
                .CopyChannel(Channel.Both) // -c:v copy -c:a copy
                .SelectStream(0, 0, Channel.Video) // -map 0:v:0
                .SelectStream(0, 1, Channel.Audio)) // -map 1:a:0
            .ProcessSynchronously(ffMpegOptions: _ffOptions);

        TryDeleteFile(_audioTmpFile!);
        TryDeleteFile(_videoTmpFile);
    }

    private void CloseVideo()
    {
        _videoFramesSubject.OnCompleted();
        if (!_videoTask.GetAwaiter().GetResult())
        {
            throw new Exception();
        }
    }

    private void CloseAudio()
    {
        if (_audioSamplesSubject != null && _audioTask != null)
        {
            _audioSamplesSubject.OnCompleted();
            if (!_audioTask.GetAwaiter().GetResult())
            {
                throw new Exception();
            }
        }
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
