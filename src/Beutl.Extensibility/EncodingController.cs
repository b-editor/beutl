using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

public abstract class EncodingController(string outputFile)
{
    public string OutputFile { get; } = outputFile;

    public abstract VideoEncoderSettings VideoSettings { get; }

    public abstract AudioEncoderSettings AudioSettings { get; }

    /// <summary>
    /// Encodes the frames and samples produced by the given providers to <see cref="OutputFile"/>.
    /// </summary>
    /// <remarks>
    /// The caller retains ownership of <paramref name="frameProvider"/> and <paramref name="sampleProvider"/>
    /// and is responsible for disposing them after this method returns or throws. Implementations are
    /// consumers, not owners: they must <b>not</b> call <see cref="IDisposable.Dispose"/> on either argument.
    /// Disposing a provider tears down resources the caller still owns (e.g. it cancels a background producer
    /// task feeding the provider), so the usual "the last user disposes" heuristic does not apply here.
    /// </remarks>
    public abstract ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken);
}
