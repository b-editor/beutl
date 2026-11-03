using Beutl.Media.Encoding;

namespace Beutl.Extensibility;

public abstract class EncodingController(string outputFile)
{
    public string OutputFile { get; } = outputFile;

    /// <summary>
    /// Gets the files <see cref="Encode"/> wrote, which Beutl publishes together after it returns.
    /// By default this is only <see cref="OutputFile"/>.
    /// </summary>
    /// <remarks>
    /// To write several files, such as an image sequence, write them next to <see cref="OutputFile"/> and return
    /// their paths here. That folder may be a temporary one: each file reaches the folder the user chose under
    /// its own name, and a failed or cancelled export changes nothing there, unless an earlier file cannot be put
    /// back, which the export's error then names. Files in other folders are refused.
    /// This is read after <see cref="Encode"/> returns, on any thread.
    /// </remarks>
    public virtual IReadOnlyList<string> OutputFiles => [OutputFile];

    public abstract VideoEncoderSettings VideoSettings { get; }

    public abstract AudioEncoderSettings AudioSettings { get; }

    /// <summary>Whether the selected settings and container use the graphics pass.</summary>
    public virtual bool IsVideoEnabled => true;

    /// <summary>Whether the selected settings and container use the audio pass.</summary>
    public virtual bool IsAudioEnabled => true;

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
