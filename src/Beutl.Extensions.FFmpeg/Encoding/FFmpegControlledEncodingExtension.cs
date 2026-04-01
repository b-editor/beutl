using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Properties;

namespace Beutl.Extensions.FFmpeg.Encoding;

[Export]
[Display(Name = nameof(Strings.FFmpegEncoder), ResourceType = typeof(Strings))]
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
#if FFMPEG_OUT_OF_PROCESS
        return new FFmpegEncodingControllerProxy(file, Settings);
#else
        return new FFmpegEncodingController(file, Settings);
#endif
    }


    public override void Load()
    {
#if !FFMPEG_OUT_OF_PROCESS
        FFmpegLoader.Initialize();
#endif
        base.Load();
    }
}
