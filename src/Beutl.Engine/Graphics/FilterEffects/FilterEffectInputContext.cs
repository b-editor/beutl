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
    // The previous reconciliation's sessions and sources. Reusing them keeps each sample's media readers
    // and caches open across frames instead of reopening them for every sample of every frame.
    private readonly List<FilterEffectInputSlot>? _recycled;
    private bool _disposed;

    internal FilterEffectInputContext(Drawable.Resource source, CompositionContext context,
        IReadOnlyList<FilterEffectInputChain.Entry> chain, int index, List<FilterEffectInputSlot>? recycled = null)
    {
        _source = source;
        _flow = FlowNode.Capture(source)!;
        EvaluationContext = context.WithTime(context.Time);
        Effect = chain[index].Resource;
        _boundary = new(source.RequireOriginal(), chain[index].Location);
        _suffix = new(chain.Skip(index + 1));
        _recycled = recycled;
    }

    /// <summary>A captured clock, output rate, decode policy, domain and cancellation token for this input.</summary>
    public CompositionContext EvaluationContext { get; }

    internal FilterEffect.Resource Effect { get; }

    /// <summary>Evaluates and retains an independent input at <paramref name="time"/>.</summary>
    /// <remarks>
    /// Repeated times reuse the completed sample within this context. The context owns the returned sample;
    /// disposing it early is optional. It must not be used after the owning drawable is reconciled again.
    /// The next reconciliation reevaluates the sample resources at the nearest offset from the clock in place,
    /// so sources keep their media readers. A disabled or out-of-range drawable produces a transparent sample.
    /// An absent boundary in a switched chain means that chain's complete output; an active sampling
    /// boundary always stops recursive sampling.
    /// </remarks>
    public FilterEffectInputSample Evaluate(MediaTime time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EvaluationContext.CancellationToken.ThrowIfCancellationRequested();
        if (_samples.TryGetValue(time, out var ready) && !ready.IsDisposed) return ready;

        MediaTime offset = time - EvaluationContext.Time;
        FilterEffectInputSlot slot = ClaimSlot(offset)
            ?? new FilterEffectInputSlot(EvaluationContext.CreateEvaluationSession());
        FilterEffectInputSample? sample = null;
        try
        {
            slot.Offset = offset;
            slot.Session.Update(EvaluationContext, time);
            CompositionContext context = slot.Session.Context;
            context.SampledDrawable = (Drawable)_flow.Object;
            context.InputBoundaries = [.. EvaluationContext.InputBoundaries, _boundary];
            var range = _flow.Object.TimeRange.Value;
            bool visible = range.Duration <= MediaTime.Zero || range.Contains(time);
            if (visible)
            {
                bool changed = false;
                _flow.Reconcile(context, ref slot.Source, ref changed);
            }
            // A hidden sample keeps its slot's source unevaluated for when the drawable is visible again.
            Drawable.Resource? source = visible ? slot.Source : null;
            sample = new FilterEffectInputSample(time, source, slot);
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
            else slot.Dispose();
            throw;
        }
    }

    // Samples usually keep their offsets from frame to frame, so the nearest offset continues the same
    // reader position instead of seeking another sample's.
    private FilterEffectInputSlot? ClaimSlot(MediaTime offset)
    {
        if (_recycled is not { Count: > 0 } pool) return null;
        int best = 0;
        for (int i = 1; i < pool.Count; i++)
        {
            if (Distance(pool[i].Offset, offset) < Distance(pool[best].Offset, offset)) best = i;
        }
        FilterEffectInputSlot slot = pool[best];
        pool.RemoveAt(best);
        return slot;

        static MediaTime Distance(MediaTime a, MediaTime b) => a > b ? a - b : b - a;
    }

    /// <summary>Ends this context and its samples, handing what they evaluated to the next reconciliation.</summary>
    internal void Recycle(List<FilterEffectInputSlot> pool)
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var sample in _samples.Values)
        {
            if (!sample.IsDisposed) pool.Add(sample.Detach());
        }
        _samples.Clear();
        _suffix.Dispose();
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
    private readonly FilterEffectInputSlot _slot;
    private readonly Drawable.Resource? _source;

    internal FilterEffectInputSample(MediaTime time, Drawable.Resource? source, FilterEffectInputSlot slot)
    {
        Time = time;
        _source = source;
        _slot = slot;
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
        _slot.Session.Context.CancellationToken.ThrowIfCancellationRequested();
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
        _slot.Dispose();
    }

    // Ends this sample without releasing what it evaluated, so the next reconciliation can reuse it.
    internal FilterEffectInputSlot Detach()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsDisposed = true;
        return _slot;
    }
}

// One sample's evaluation session and the source resource evaluated in it. It outlives its sample when the
// next reconciliation reuses it, which keeps the source's media readers and the session's caches open.
internal sealed class FilterEffectInputSlot(CompositionEvaluationSession session) : IDisposable
{
    public CompositionEvaluationSession Session { get; } = session;

    public Drawable.Resource? Source;

    // The sample time relative to the clock of the input that last evaluated this slot.
    public MediaTime Offset;

    public static void DisposeAll(List<FilterEffectInputSlot> slots)
    {
        List<Exception>? failures = null;
        foreach (var slot in slots)
        {
            try { slot.Dispose(); }
            catch (Exception ex) { CleanupFailures.Append(failures ??= [], ex); }
        }
        slots.Clear();
        CleanupFailures.ThrowIfAny(failures, "Failed to release evaluated effect inputs.");
    }

    public void Dispose()
    {
        try { Source?.Dispose(); }
        finally
        {
            Source = null;
            Session.Dispose();
        }
    }
}
