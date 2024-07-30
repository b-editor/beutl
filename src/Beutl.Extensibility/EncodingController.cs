using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

public abstract class EncodingController(string outputFile)
{
    public string OutputFile { get; } = outputFile;

    public abstract VideoEncoderSettings VideoSettings { get; }

    public abstract AudioEncoderSettings AudioSettings { get; }

    public abstract ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken);
}
