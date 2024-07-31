using Beutl.Extensibility;
using Beutl.Media.Encoding;

namespace Beutl.Embedding.FFmpeg.ControlledEncoding;

[Export]
public class FFmpegControlledEncodingExtension : ControllableEncodingExtension
{
    public override string Name => "FFmpeg Encoding";

    public override string DisplayName => "FFmpeg Encoding";

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
        return new FFmpegEncodingController(file);
    }


    public override void Load()
    {
        FFmpegLoader.Initialize();
        base.Load();
    }
}
