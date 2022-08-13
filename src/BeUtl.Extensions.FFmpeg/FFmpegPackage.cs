using BeUtl.Extensions.FFmpeg;
using BeUtl.Extensions.FFmpeg.Decoding;
using BeUtl.Framework;

[assembly: PackageAware(typeof(FFmpegPackage))]

namespace BeUtl.Extensions.FFmpeg;

public sealed class FFmpegPackage : Package
{
    public override IEnumerable<Extension> GetExtensions()
    {
        yield return new FFmpegDecodingExtension();
    }
}
