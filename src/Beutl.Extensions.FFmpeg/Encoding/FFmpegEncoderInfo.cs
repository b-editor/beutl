using Beutl.Media.Encoding;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegEncoderInfo(FFmpegEncodingSettings settings) : IEncoderInfo
{
    public string Name => "FFmpeg Encoder";

    public MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        return new FFmpegWriter(file, videoConfig, audioConfig, settings);
    }

    public AudioEncoderSettings DefaultAudioConfig()
    {
        return new FFmpegAudioEncoderSettings();
    }

    public VideoEncoderSettings DefaultVideoConfig()
    {
        return new FFmpegVideoEncoderSettings();
    }

    public IEnumerable<string> SupportExtensions()
    {
        yield return ".mp4";
        yield return ".wav";
        yield return ".mp3";
        yield return ".wmv";
        yield return ".avi";
        yield return ".webm";
        yield return ".3gp";
        yield return ".3g2";
        yield return ".flv";
        yield return ".mkv";
        yield return ".mov";
        yield return ".ogv";
    }
}
