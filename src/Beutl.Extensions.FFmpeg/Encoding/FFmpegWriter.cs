using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;

using FFMpegCore;
using FFMpegCore.Enums;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed unsafe class FFmpegWriter : MediaWriter
{
    private readonly FFmpegEncodingSettings _settings;
    private readonly string _outputFile;

    private IFFmpegVideoWriter? _videoWriter;
    private string? _videoTmpFile;

    //private IFFmpegAudioWriter? _audioWriter;
    private FFmpegOutprocessAudioWriter? _audioWriter;
    private string? _audioTmpFile;

    public FFmpegWriter(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig, FFmpegEncodingSettings settings)
        : base(videoConfig, audioConfig)
    {
        _settings = settings;
        _outputFile = file;
    }

    public override long NumberOfFrames => _videoWriter?.NumberOfFrames ?? 0;

    public override long NumberOfSamples => _audioWriter?.NumberOfSamples ?? 0;

    public override bool AddVideo(IBitmap image)
    {
        if (_videoWriter == null)
        {
            _videoTmpFile = Path.GetTempFileName();
            TryDeleteFile(_videoTmpFile);
            _videoTmpFile = Path.ChangeExtension(_videoTmpFile, Path.GetExtension(_outputFile));
            if (OperatingSystem.IsWindows())
            {
                _videoWriter = new FFmpegOutprocessVideoWriter(_videoTmpFile, (FFmpegVideoEncoderSettings)VideoConfig, _settings);
            }
            else
            {
                _videoWriter = new FFmpegInprocessVideoWriter(_videoTmpFile, (FFmpegVideoEncoderSettings)VideoConfig);
            }
        }

        return _videoWriter.AddVideo(image);
    }

    public override bool AddAudio(IPcm sound)
    {
        if (_audioWriter == null)
        {
            _audioTmpFile = Path.GetTempFileName();
            TryDeleteFile(_audioTmpFile);
            _audioTmpFile = Path.ChangeExtension(_audioTmpFile, Path.GetExtension(_outputFile));
            _audioWriter = new FFmpegOutprocessAudioWriter(_audioTmpFile, (FFmpegAudioEncoderSettings)AudioConfig, _settings);
        }

        return _audioWriter.AddAudio(sound);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            _videoWriter?.Dispose();
            _audioWriter?.Dispose();
        }
        catch
        {
            if (disposing)
            {
                throw;
            }
        }

        if (disposing)
        {
            if (_videoTmpFile != null)
            {
                if (_audioTmpFile != null)
                {
                    Composite();
                }
                else
                {
                    File.Move(_videoTmpFile, _outputFile, true);
                }
            }
        }
    }

    private void Composite()
    {
        var ffOptions = new FFOptions() { BinaryFolder = Path.GetDirectoryName(FFmpegLoader.GetExecutable())!, UseCache = false };
        FFMpegArguments.FromFileInput(_videoTmpFile!)
            .AddFileInput(_audioTmpFile!)
            .OutputToFile(_outputFile, addArguments: o => o
                .CopyChannel(Channel.Both) // -c:v copy -c:a copy
                .SelectStream(0, 0, Channel.Video) // -map 0:v:0
                .SelectStream(0, 1, Channel.Audio)) // -map 1:a:0
            .ProcessSynchronously(ffMpegOptions: ffOptions);

        TryDeleteFile(_audioTmpFile!);
        TryDeleteFile(_videoTmpFile!);
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
