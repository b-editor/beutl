using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;

using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

using FFmpeg.AutoGen;

using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed unsafe class FFmpegOutprocessAudioWriter : IFFmpegAudioWriter
{
    private readonly FFmpegEncodingSettings _settings;
    private readonly FFOptions _ffOptions;

    private CustomRawAudioPipeSource? _audioSamplesSource;
    private Task<bool>? _audioTask;
    private Subject<IAudioSample>? _audioSamplesSubject;
    private PipeStream? _stream;
    private readonly string _formatName;
    private readonly AVOutputFormat* _outputFormat;
    private readonly string _outputFile;

    public FFmpegOutprocessAudioWriter(string file, FFmpegAudioEncoderSettings audioConfig, FFmpegEncodingSettings settings)
    {
        try
        {
            AudioConfig = audioConfig;
            _settings = settings;
            _ffOptions = new() { BinaryFolder = Path.GetDirectoryName(FFmpegLoader.GetExecutable())!, UseCache = false };
            GlobalFFOptions.Configure(_ffOptions);

            _outputFile = file;

            _outputFormat = ffmpeg.av_guess_format(null, Path.GetFileName(file), null);
            if (_outputFormat == null)
                throw new Exception("av_guess_format failed");

            _formatName = UTF8Marshaler.FromNative(System.Text.Encoding.UTF8, _outputFormat->name);

            // 音声ストリームは遅延初期化
        }
        catch
        {
            throw;
        }
    }

    public long NumberOfSamples { get; private set; }

    public FFmpegAudioEncoderSettings AudioConfig { get; }

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

    private void ConfigureAudioOutputArguments(FFMpegArgumentOptions options)
    {
        var audioConfig = AudioConfig;
        var audioCodec = _outputFormat->audio_codec;

        AVCodec* codec = ffmpeg.avcodec_find_encoder((AVCodecID)AudioConfig.Codec);
        if (codec != null
            && codec->type == AVMediaType.AVMEDIA_TYPE_AUDIO
            && codec->id != AVCodecID.AV_CODEC_ID_NONE)
        {
            audioCodec = codec->id;
        }

        var args = AudioConfig.Arguments;

        var audioCodecName = ffmpeg.avcodec_get_name(audioCodec);

        options
            .WithAudioBitrate(audioConfig.Bitrate)
            .WithAudioSamplingRate(AudioConfig.SampleRate)
            .WithCustomArgument($"-ac {AudioConfig.Channels}")
            .ForceFormat(_formatName)
            .WithAudioCodec(audioCodecName)
            .When(!string.IsNullOrWhiteSpace(args), o => o.WithCustomArgument(args!));
    }

    [MemberNotNull(nameof(_audioSamplesSubject), nameof(_audioSamplesSource), nameof(_audioTask))]
    private void LazyInitializeAudioStream(int sampleRate, int channels, string format)
    {
        _audioSamplesSubject = new Subject<IAudioSample>();
        _audioSamplesSource = new CustomRawAudioPipeSource(_audioSamplesSubject.ToEnumerable(), s => _stream = s as PipeStream)
        {
            SampleRate = (uint)sampleRate,
            Channels = (uint)channels,
            Format = format
        };

        _audioTask = Task.Factory.StartNew(() => FFMpegArguments.FromPipeInput(_audioSamplesSource, ConfigureInputArguments)
            .OutputToFile(_outputFile, true, ConfigureAudioOutputArguments)
            .ProcessSynchronously(ffMpegOptions: _ffOptions),
            TaskCreationOptions.LongRunning);
    }

    public bool AddAudio(IPcm sound)
    {
        if (_audioSamplesSubject == null
            || _audioSamplesSource == null
            || _audioTask == null)
        {
            LazyInitializeAudioStream(sound.SampleRate, 2, "f32le");
        }

        _audioSamplesSubject.OnNext(new CustomAudioSample(
            (sound as Pcm<Stereo32BitFloat>)?.Clone() ?? sound.Convert<Stereo32BitFloat>()));

        NumberOfSamples += sound.NumSamples;

        if (_audioTask.IsFaulted || _audioTask.IsCompleted)
        {
            _audioTask.GetAwaiter().GetResult();
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
        if (_audioSamplesSubject != null && _audioTask != null)
        {
            _audioSamplesSubject.OnCompleted();
            _audioTask.GetAwaiter().GetResult();
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
