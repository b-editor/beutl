using Beutl.Extensibility;
using System.ComponentModel.DataAnnotations;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

[Export]
[Display(Name = "FFmpeg Encoder")]
public class FFmpegControlledEncodingExtension : ControllableEncodingExtension
{
    public override FFmpegEncodingSettings Settings { get; } = new();

    public override IEnumerable<string> SupportExtensions()
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

    public override EncodingController CreateController(string file)
    {
        return new FFmpegEncodingController(file, Settings);
    }


    public override void Load()
    {
        FFmpegLoader.Initialize();
        base.Load();
    }
}
