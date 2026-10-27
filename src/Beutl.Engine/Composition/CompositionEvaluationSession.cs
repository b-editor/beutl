namespace Beutl.Composition;

/// <summary>
/// Owns a time-sample evaluation scope. Hosts with scene-dependent evaluation override the factory on
/// <see cref="CompositionContext"/> to isolate their resource caches as well as their clock and flow.
/// </summary>
public class CompositionEvaluationSession(CompositionContext context) : IDisposable
{
    public CompositionContext Context { get; } = context;

    public virtual void Update(CompositionContext source, MediaTime time)
    {
        source.CancellationToken.ThrowIfCancellationRequested();
        Context.Time = time;
        Context.Flow = null;
        Context.ReplayedFlow = null;
        Context.ContainsScene3D = false;
        Context.DisableResourceShare = source.DisableResourceShare;
        Context.PreferProxy = source.PreferProxy;
        Context.PreferredProxyPreset = source.PreferredProxyPreset;
        Context.TargetDomain = source.TargetDomain;
        Context.FrameRate = source.FrameRate;
        Context.CancellationToken = source.CancellationToken;
    }

    public virtual void Dispose()
    {
    }
}
