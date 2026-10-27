using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Effects;

/// <summary>The evaluated input immediately before one occurrence of an effect in a drawable's chain.</summary>
/// <remarks>
/// Supplied to <see cref="FilterEffect.Resource.PrepareInput"/> after the complete drawable, including
/// consumed flow, has been evaluated. Sample times use <see cref="EvaluationContext"/>'s clock. Samples
/// include source transforms, opacity and preceding effects in the drawable's parent coordinates.
/// The engine applies the owning drawable's blend mode and the following effects once to RenderInput's
/// output. The context and its samples live until the next reconciliation or disposal of the drawable.
/// </remarks>
public sealed class FilterEffectInputContext : IDisposable
{
    private readonly Drawable.Resource _source;
    private readonly FlowNode _flow;
    private readonly FilterEffectInputBoundary _boundary;
    private readonly FilterEffectInputChain _suffix;
    private readonly Dictionary<MediaTime, FilterEffectInputSample> _samples = [];
    private bool _disposed;

    internal FilterEffectInputContext(Drawable.Resource source, CompositionContext context,
        IReadOnlyList<FilterEffectInputChain.Entry> chain, int index)
    {
        _source = source;
        _flow = FlowNode.Capture(source)!;
        EvaluationContext = context.WithTime(context.Time);
        Effect = chain[index].Resource;
        _boundary = new(source.RequireOriginal(), chain[index].Location);
        _suffix = new(chain.Skip(index + 1));
    }

    /// <summary>A captured clock, output rate, decode policy, domain and cancellation token for this input.</summary>
    public CompositionContext EvaluationContext { get; }

    internal FilterEffect.Resource Effect { get; }

    /// <summary>Evaluates and retains an independent input at <paramref name="time"/>.</summary>
    /// <remarks>
    /// Repeated times reuse the completed sample within this context. The context owns the returned sample;
    /// disposing it early is optional. It must not be used after the owning drawable is reconciled again.
    /// A disabled or out-of-range drawable produces a transparent sample. An absent boundary in a switched
    /// chain means that chain's complete output; an active sampling boundary always stops recursive sampling.
    /// </remarks>
    public FilterEffectInputSample Evaluate(MediaTime time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EvaluationContext.CancellationToken.ThrowIfCancellationRequested();
        if (_samples.TryGetValue(time, out var ready) && !ready.IsDisposed) return ready;

        CompositionEvaluationSession session = EvaluationContext.CreateEvaluationSession();
        Drawable.Resource? source = null;
        FilterEffectInputSample? sample = null;
        try
        {
            session.Update(EvaluationContext, time);
            CompositionContext context = session.Context;
            context.SampledDrawable = (Drawable)_flow.Object;
            context.InputBoundaries = [.. EvaluationContext.InputBoundaries, _boundary];
            var range = _flow.Object.TimeRange.Value;
            bool visible = range.Duration <= MediaTime.Zero || range.Contains(time);
            if (visible)
            {
                bool changed = false;
                _flow.Reconcile(context, ref source, ref changed);
            }
            sample = new FilterEffectInputSample(time, source, session);
            if (source != null)
            {
                source.BlendMode = BlendMode.SrcOver;
                source.PrepareFilterEffectInput(context, source.EvaluatedInputChain);
            }
            context.CancellationToken.ThrowIfCancellationRequested();
            _samples[time] = sample;
            EvaluationContext.ContainsScene3D |= context.ContainsScene3D;
            return sample;
        }
        catch
        {
            if (sample != null) sample.Dispose();
            else
            {
                source?.Dispose();
                session.Dispose();
            }
            throw;
        }
    }

    internal void Render(GraphicsContext2D context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EvaluationContext.CancellationToken.ThrowIfCancellationRequested();
        using (context.PushBlendMode(_source.BlendMode))
        using (context.PushFilterEffect(_suffix.Resource))
        using (context.PushNode(true,
                   static value => new DrawableGroup.ContentIsolationRenderNode(value),
                   static (node, value) => node.Update(value)))
        {
            Effect.RenderInput(context, this);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        foreach (var sample in _samples.Values)
        {
            try { sample.Dispose(); }
            catch (Exception ex) { CleanupFailures.Append(failures ??= [], ex); }
        }
        _samples.Clear();
        _suffix.Dispose();
        CleanupFailures.ThrowIfAny(failures, "Failed to release evaluated effect inputs.");
    }
}

/// <summary>An owned, evaluated source and effect prefix for one time.</summary>
/// <remarks>The input context releases it automatically at the next reconciliation. Disposal is idempotent.</remarks>
public sealed class FilterEffectInputSample : IDisposable
{
    private readonly CompositionEvaluationSession _session;
    private readonly Drawable.Resource? _source;

    internal FilterEffectInputSample(MediaTime time, Drawable.Resource? source, CompositionEvaluationSession session)
    {
        Time = time;
        _source = source;
        _session = session;
    }

    public MediaTime Time { get; }
    public bool IsDisposed { get; private set; }
    public bool IsVisible => !IsDisposed && _source is { IsEnabled: true };

    /// <summary>The independent evaluated resource. Treat it as read-only; the sample owns it.</summary>
    public Drawable.Resource? Resource
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _source;
        }
    }

    /// <summary>Records an isolated sample in parent coordinates, including its own transform and opacity.</summary>
    /// <remarks>Isolation follows the sample's actual bounds; it does not expand to or clip at the current frame.</remarks>
    public void Draw(GraphicsContext2D context)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _session.Context.CancellationToken.ThrowIfCancellationRequested();
        if (_source == null) return;
        using (context.PushNode(true,
                   static value => new DrawableGroup.ContentIsolationRenderNode(value),
                   static (node, value) => node.Update(value)))
            context.DrawDrawable(_source);
    }

    /// <summary>Builds a dependency root without starting a renderer or allocating a render target.</summary>
    /// <remarks>The caller owns the returned node and must dispose it before disposing this sample.</remarks>
    public ContainerRenderNode CreateRenderNode(Size availableSize, float outputScale = 1)
    {
        var node = new ContainerRenderNode();
        try
        {
            using var context = new GraphicsContext2D(node, availableSize, outputScale);
            Draw(context);
            return node;
        }
        catch
        {
            node.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        try { _source?.Dispose(); }
        finally { _session.Dispose(); }
    }
}
