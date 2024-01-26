using Beutl.Extensions.MediaFoundation.Properties;
using Beutl.Media.Decoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public sealed class MFDecoderInfo(MFDecodingExtension extension) : IDecoderInfo
{
    public string Name => Strings.DecodingName;

    //https://learn.microsoft.com/ja-jp/windows/win32/medfound/supported-media-formats-in-media-foundation
    public IEnumerable<string> AudioExtensions()
    {
        yield return ".mp3";
        yield return ".wav";
        yield return ".m4a";
        yield return ".aac";
        yield return ".wma";
        yield return ".sami";
        yield return ".smi";
        yield return ".m4v";
        yield return ".mov";
        yield return ".mp4";
        yield return ".avi";
        yield return ".adts";
        yield return ".asf";
        yield return ".wmv";
        yield return ".3gp";
        yield return ".3gp2";
        yield return ".3gpp";
    }

    public MediaReader? Open(string file, MediaOptions options)
    {
        try
        {
            return MFThread.Dispatcher.Invoke(() => new MFReader(file, options, extension));
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> VideoExtensions()
    {
        yield return ".mp4";
        yield return ".mov";
        yield return ".m4v";
        yield return ".avi";
        yield return ".wmv";
        yield return ".sami";
        yield return ".smi";
        yield return ".adts";
        yield return ".asf";
        yield return ".3gp";
        yield return ".3gp2";
        yield return ".3gpp";
    }
}
