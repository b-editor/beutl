#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public class CodecRecord(string name, string longName) : IEquatable<CodecRecord?>
{
    public static readonly CodecRecord Default = new("Default", "Default");

    public string Name => name;

    public string LongName => longName;

    public override bool Equals(object? obj)
    {
        return obj is CodecRecord codec && Equals(codec);
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }

    public bool Equals(CodecRecord? other)
    {
        return other?.Name == Name;
    }

    public override string ToString() => LongName;
}
