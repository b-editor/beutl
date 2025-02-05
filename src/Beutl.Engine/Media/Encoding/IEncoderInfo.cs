namespace Beutl.Media.Encoding;

[Obsolete("Use EncodingController instead.")]
public interface IEncoderInfo
{
    string Name { get; }

    MediaWriter? Create(string file, VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig);

    bool IsSupported(string file)
    {
        return SupportExtensions().Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    }

    IEnumerable<string> SupportExtensions();

    VideoEncoderSettings DefaultVideoConfig();

    AudioEncoderSettings DefaultAudioConfig();
}
