using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Composition;

/// <summary>Owns independent resource evaluation for a time sample.</summary>
/// <remarks>
/// Hosts override <see cref="CompositionContext.CreateEvaluationSession"/> when evaluating a property can
/// access host-owned caches. The session uses the same document snapshot as its source, never the live
/// editor document in place of an export snapshot. Dispose evaluated resources before disposing the session.
/// The engine updates a session again for later samples, including those of later frames, so its resources
/// keep their media readers; <see cref="Update"/> derives every frame-dependent condition from its source.
/// </remarks>
public class CompositionEvaluationSession(CompositionContext context) : IDisposable
{
    public CompositionContext Context { get; } = context;

    public virtual void Update(CompositionContext source, MediaTime time)
    {
        source.CancellationToken.ThrowIfCancellationRequested();
        Context.Time = time;
        Context.Flow = null;
        Context.FlowOwner = null;
        Context.ReplayedFlow = null;
        Context.SampledDrawable = null;
        Context.InputBoundaries = source.InputBoundaries;
        Context.TimeRemappedTarget = source.TimeRemappedTarget;
        Context.ContainsScene3D = false;
        Context.DisableResourceShare = true;
        Context.Conditions = source.Conditions;
    }

    // Scene hosts can rebuild authored flow when the sample's inputs differ from the captured frame.
    internal virtual void Reconcile(FlowNode flow, ref Drawable.Resource? resource, ref bool changed)
        => flow.Reconcile(Context, ref resource, ref changed);

    // Called after the source and its effects have finished using the session's evaluation services.
    internal virtual void CompleteEvaluation()
    {
    }

    public virtual void Dispose()
    {
    }
}
