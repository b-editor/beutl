using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

public abstract class EncodingController(
    string outputFile,
    IFrameProvider frameProvider,
    ISampleProvider sampleProvider)
{
    public string OutputFile { get; } = outputFile;

    public ISampleProvider SampleProvider { get; } = sampleProvider;

    public IFrameProvider FrameProvider { get; } = frameProvider;

    public abstract VideoEncoderSettings VideoSettings { get; }

    public abstract AudioEncoderSettings AudioSettings { get; }

    public abstract ValueTask Encode(CancellationToken cancellationToken);
}
