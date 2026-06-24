using Beutl.Media;

namespace Beutl.Extensibility;

/// <summary>
/// Supplies decoded frames to an encoder.
/// </summary>
/// <remarks>
/// Implementations are <see cref="IDisposable"/> so they can be torn down deterministically.
/// <see cref="IDisposable.Dispose"/> must drain (observe) any in-flight prefetch the provider
/// started so a faulted background task cannot later surface as an
/// <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>.
/// </remarks>
public interface IFrameProvider : IDisposable
{
    public long FrameCount { get; }

    public Rational FrameRate { get; }

    public ValueTask<Bitmap> RenderFrame(long frame);
}
