using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

/// <summary>
/// Per-evaluation context flowing through the composition graph. Carries the proxy/original
/// decode selection, the resource-sharing toggle, and the resolve-time proxy density cap.
/// </summary>
/// <remarks>
/// Output conditions live in the immutable <see cref="Conditions"/>; the individual properties replace it.
/// They and <see cref="DisableResourceShare"/> are <c>set</c>, not <c>init</c>, because node-graph render nodes reuse a cached context.
/// </remarks>
public class CompositionContext(MediaTime time) : EvaluationContext(time)
{
    // A fresh instance per access, not a shared singleton: Time / DisableResourceShare / PreferProxy /
    // PreferredProxyPreset are all mutable, so a caller that writes to a context it received must not be
    // able to corrupt a global baseline for every other render.
    public static CompositionContext Default => new(MediaTime.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    // Set while reconciling resources, including drawables held by brushes and effects.
    internal bool ContainsScene3D { get; set; }

    internal FlowNode? ReplayedFlow { get; set; }

    internal Drawable? SampledDrawable { get; set; }

    internal IReadOnlyList<FilterEffectInputBoundary> InputBoundaries { get; set; } = [];

    // Set while a time controller evaluates its target at a remapped clock, which the target's authored
    // TimeRange no longer bounds.
    internal bool IsTimeRemapped { get; set; }

    /// <summary>Creates an isolated resource-evaluation session retaining the current host and snapshot.</summary>
    public virtual CompositionEvaluationSession CreateEvaluationSession() => new(WithTime(Time));

    public bool DisableResourceShare { get; set; }

    /// <summary>The output conditions this evaluation inherits; assign it to pass every condition on at once.</summary>
    public CompositionConditions Conditions { get; set; } = CompositionConditions.Default;

    /// <inheritdoc cref="CompositionConditions.FrameRate"/>
    public Rational FrameRate
    {
        get => Conditions.FrameRate;
        set => Conditions = Conditions with { FrameRate = value };
    }

    /// <inheritdoc cref="CompositionConditions.CancellationToken"/>
    public CancellationToken CancellationToken
    {
        get => Conditions.CancellationToken;
        set => Conditions = Conditions with { CancellationToken = value };
    }

    /// <inheritdoc cref="CompositionConditions.PreferProxy"/>
    public bool PreferProxy
    {
        get => Conditions.PreferProxy;
        set => Conditions = Conditions with { PreferProxy = value };
    }

    /// <inheritdoc cref="CompositionConditions.PreferredProxyPreset"/>
    public ProxyPreset PreferredProxyPreset
    {
        get => Conditions.PreferredProxyPreset;
        set => Conditions = Conditions with { PreferredProxyPreset = value };
    }

    /// <inheritdoc cref="CompositionConditions.TargetDomain"/>
    public Rect? TargetDomain
    {
        get => Conditions.TargetDomain;
        set => Conditions = Conditions with { TargetDomain = value };
    }

    /// <inheritdoc cref="CompositionConditions.EvaluationServices"/>
    public ICompositionEvaluationServices? EvaluationServices
    {
        get => Conditions.EvaluationServices;
        set => Conditions = Conditions with { EvaluationServices = value };
    }

    /// <summary>Captures an independent evaluation clock while preserving this context's services and policies.</summary>
    /// <remarks>The captured context belongs to the current render request; it must not outlive its services.</remarks>
    public virtual CompositionContext WithTime(MediaTime time)
    {
        var result = (CompositionContext)MemberwiseClone();
        result.Time = time;
        result.DisableResourceShare = true;
        result.Flow = null;
        result.ReplayedFlow = null;
        return result;
    }

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property is IExpressionProperty<T> expressionProperty ? expressionProperty.Evaluate(this) : property.Value;
    }
}
