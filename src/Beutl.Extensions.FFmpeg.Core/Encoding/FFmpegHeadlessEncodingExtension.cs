using Beutl.Extensibility;

namespace Beutl.Extensions.FFmpeg.Encoding;

public sealed class FFmpegHeadlessEncodingExtension : ControllableEncodingExtension
{
    // Shared with FFmpegControlledEncodingExtension, the encoder the app registers.
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".mp4", ".wav", ".mp3", ".wmv", ".avi", ".webm", ".3gp", ".3g2", ".flv", ".mkv", ".mov", ".ogv"];

    public override FFmpegEncodingSettings Settings { get; } = new();

    public override IEnumerable<string> SupportExtensions() => SupportedExtensions;

    public override EncodingController CreateController(string file)
    {
        return new FFmpegEncodingControllerProxy(file, Settings);
    }
}
