using Beutl.Media.Encoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

public class MFEncoderInfo : IEncoderInfo
{
    public string Name => "Media Foundation Encoder";

    public MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        if (videoConfig is not MFVideoEncoderSettings mfVideoConfig)
            return null;

        return new MFWriter(file, mfVideoConfig, audioConfig);
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

    public VideoEncoderSettings DefaultVideoConfig() => new MFVideoEncoderSettings();

    public AudioEncoderSettings DefaultAudioConfig() => new AudioEncoderSettings();
}
