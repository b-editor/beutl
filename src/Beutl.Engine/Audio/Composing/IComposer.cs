using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public interface IComposer : IDisposable
{
    bool IsAudioRendering { get; }

    bool IsDisposed { get; }

    int SampleRate { get; }

    AudioBuffer? Compose(TimeRange range, CompositionFrame frame);

    /// <summary>Reports the largest latency among the nodes retained by the latest composition.</summary>
    int GetTotalLatencySamples(int sampleRate);

    /// <summary>Reports the drain latency still held by nodes retained by the latest composition.</summary>
    int GetDrainLatencySamples(int sampleRate);

    /// <summary>Drains retained node tails into a buffer covering <paramref name="range"/>.</summary>
    /// <param name="eligibility">
    /// The current eligibility snapshot for retained sounds. When omitted, no retained sound is
    /// considered eligible for draining.
    /// </param>
    AudioBuffer? Flush(TimeRange range, CompositionEligibility? eligibility = null);
}
