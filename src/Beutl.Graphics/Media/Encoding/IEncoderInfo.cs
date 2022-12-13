namespace Beutl.Media.Encoding;

public interface IEncoderInfo
{
    string Name { get; }

    MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig);

    bool IsSupported(string file)
    {
        return SupportExtensions().Contains(Path.GetExtension(file));
    }

    IEnumerable<string> SupportExtensions();

    VideoEncoderSettings DefaultVideoConfig();

    AudioEncoderSettings DefaultAudioConfig();
}
