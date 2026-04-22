using Beutl.Configuration;

namespace Beutl.Services.Proxy;

public readonly record struct ProxyFFmpegOptions(
    string Extension,
    string VideoFilter,
    IReadOnlyList<string> VideoCodecArgs,
    IReadOnlyList<string> AudioCodecArgs);

public static class ProxyPresetOptions
{
    public static ProxyFFmpegOptions For(ProxyPresetKind kind) => kind switch
    {
        ProxyPresetKind.HalfH264 => new(
            Extension: ".mp4",
            VideoFilter: "scale=trunc(iw/2/2)*2:trunc(ih/2/2)*2",
            VideoCodecArgs: new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-pix_fmt", "yuv420p" },
            AudioCodecArgs: new[] { "-c:a", "aac", "-b:a", "128k" }),
        ProxyPresetKind.QuarterH264 => new(
            Extension: ".mp4",
            VideoFilter: "scale=trunc(iw/4/2)*2:trunc(ih/4/2)*2",
            VideoCodecArgs: new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-pix_fmt", "yuv420p" },
            AudioCodecArgs: new[] { "-c:a", "aac", "-b:a", "128k" }),
        ProxyPresetKind.ProResProxy => new(
            Extension: ".mov",
            VideoFilter: "scale=trunc(iw/2/2)*2:trunc(ih/2/2)*2",
            VideoCodecArgs: new[] { "-c:v", "prores_ks", "-profile:v", "0", "-pix_fmt", "yuv422p10le" },
            AudioCodecArgs: new[] { "-c:a", "pcm_s16le" }),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
