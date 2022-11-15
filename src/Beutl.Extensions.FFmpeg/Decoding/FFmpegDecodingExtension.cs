using System.Reflection;

using Beutl.Framework;
using Beutl.Media.Decoding;

using FFmpeg.AutoGen;

namespace Beutl.Extensions.FFmpeg.Decoding;

[Export]
public class FFmpegDecodingExtension : DecodingExtension
{
    public override string Name => "FFmpeg Decoder";

    public override string DisplayName => "FFmpeg Decoder";

    public override IDecoderInfo GetDecoderInfo()
    {
        return FFmpegDecoderInfo.Instance;
    }

    public override void Load()
    {
        base.Load();

        if (OperatingSystem.IsWindows())
        {
            string dir = Path.Combine(
                Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native");

            ffmpeg.RootPath = dir;
        }
    }
}

public sealed class FFmpegDecoderInfo : IDecoderInfo
{
    public static readonly FFmpegDecoderInfo Instance = new();

    public string Name => "FFmpeg Decoder";

    public IEnumerable<string> AudioExtensions()
    {
        yield return ".mp3";
        yield return ".ogg";
        yield return ".wav";
        yield return ".aac";
        yield return ".wma";
        yield return ".m4a";
        yield return ".webm";
        yield return ".opus";
    }

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            return new FFmpegReader(file, options);
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield return ".avi";
        yield return ".mov";
        yield return ".wmv";
        yield return ".mp4";
        yield return ".webm";
        yield return ".mkv";
        yield return ".flv";
        yield return ".264";
        yield return ".mpeg";
        yield return ".ts";
        yield return ".mts";
        yield return ".m2ts";
    }
}
