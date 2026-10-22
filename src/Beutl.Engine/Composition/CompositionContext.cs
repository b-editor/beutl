using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

/// <summary>
/// Per-evaluation context flowing through the composition graph. Carries the proxy/original
/// decode selection, the resource-sharing toggle, and the resolve-time proxy density cap.
/// </summary>
/// <remarks>
/// <see cref="PreferProxy"/> is the only decode-selection signal consumers read.
/// It and <see cref="DisableResourceShare"/> are <c>set</c>, not <c>init</c>, because node-graph render nodes reuse a cached context.
/// <see cref="PreferredProxyPreset"/> is the resolve-time density cap passed to <see cref="ProxyResolver.Resolve(Uri, ProxyPreset)"/>.
/// </remarks>
public class CompositionContext(MediaTime time) : EvaluationContext(time)
{
    // A fresh instance per access, not a shared singleton: Time / DisableResourceShare / PreferProxy /
    // PreferredProxyPreset are all mutable, so a caller that writes to a context it received must not be
    // able to corrupt a global baseline for every other render.
    public static CompositionContext Default => new(MediaTime.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    internal FlowNode? ReplayedFlow { get; set; }

    public bool DisableResourceShare { get; set; }

    public bool PreferProxy { get; set; }

    public ProxyPreset PreferredProxyPreset { get; set; } = ProxyPreset.Quarter;

    /// <summary>
    /// Gets or sets the finite logical composition domain available to auxiliary render-node evaluation.
    /// </summary>
    /// <remarks>
    /// Scene composition supplies its frame rectangle. A null value means the caller has no finite target domain;
    /// self-bounded render graphs remain valid, while root <c>TargetRegion.Full</c> access requires a value.
    /// </remarks>
    public Rect? TargetDomain { get; set; }

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property is IExpressionProperty<T> expressionProperty ? expressionProperty.Evaluate(this) : property.Value;
    }
}
