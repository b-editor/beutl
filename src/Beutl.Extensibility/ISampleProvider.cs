using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensibility;

/// <summary>
/// Supplies decoded audio samples to an encoder.
/// </summary>
/// <remarks>
/// Implementations are <see cref="IDisposable"/> so they can be torn down deterministically.
/// <see cref="IDisposable.Dispose"/> must drain (observe) any in-flight prefetch the provider
/// started so a faulted background task cannot later surface as an
/// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>.
/// </remarks>
public interface ISampleProvider : IDisposable
{
    public long SampleCount { get; }

    public long SampleRate { get; }

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length);
}
