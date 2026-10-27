namespace Beutl.Composition;

/// <summary>Owns independent resource evaluation for a time sample.</summary>
/// <remarks>
/// Hosts override <see cref="CompositionContext.CreateEvaluationSession"/> when evaluating a property can
/// access host-owned caches. The session uses the same document snapshot as its source, never the live
/// editor document in place of an export snapshot. Dispose evaluated resources before disposing the session.
/// </remarks>
public class CompositionEvaluationSession(CompositionContext context) : IDisposable
{
    public CompositionContext Context { get; } = context;

    public virtual void Update(CompositionContext source, MediaTime time)
    {
        source.CancellationToken.ThrowIfCancellationRequested();
        Context.Time = time;
        Context.Flow = null;
        Context.ReplayedFlow = null;
        Context.SampledDrawable = null;
        Context.InputBoundaries = source.InputBoundaries;
        Context.ContainsScene3D = false;
        Context.DisableResourceShare = true;
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
