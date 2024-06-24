using System.Runtime.Versioning;
using Beutl.Media.Encoding;

namespace Beutl.Extensions.AVFoundation.Encoding;

[SupportedOSPlatform("macos")]
public sealed class AVFEncoderInfo(AVFEncodingExtension extension) : IEncoderInfo
{
    public string Name => "AVFoundation";

    public MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        try
        {
            return new AVFWriter(file, (AVFVideoEncoderSettings)videoConfig, (AVFAudioEncoderSettings)audioConfig);
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public IEnumerable<string> SupportExtensions()
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

    public VideoEncoderSettings DefaultVideoConfig()
    {
        return new AVFVideoEncoderSettings();
    }

    public AudioEncoderSettings DefaultAudioConfig()
    {
        return new AVFAudioEncoderSettings();
    }
}
