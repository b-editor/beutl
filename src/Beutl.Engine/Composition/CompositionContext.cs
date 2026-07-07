using Beutl.Engine;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

/// <summary>
/// Per-evaluation context flowing through the composition graph. Carries the proxy/original
/// decode selection, the resource-sharing toggle, and the resolve-time proxy density cap.
/// </summary>
/// <remarks>
/// <para>
/// <b>Proxy/original selection — three signals, one source of truth.</b> The "use proxy or
/// original" choice is carried by two boolean fields (<see cref="ForceOriginalSource"/> and
/// <see cref="PreferProxy"/>) plus the user-facing <c>EditorConfig.PreviewSourceMode</c> enum,
/// which is the source of truth. The two bools are a derived, transport-friendly projection of
/// that enum. The producer (SceneCompositor's nested CompositorContext) seeds
/// <c>PreferProxy = !ForceOriginalSource &amp;&amp; EditorConfig.PreviewSourceMode == PreferProxy</c>,
/// so the invariant <c>ForceOriginalSource =&gt; !PreferProxy</c> always holds. Consumers
/// (SceneDrawable, SceneSound) collapse the two into one "decode original" bit —
/// <c>ForceOriginalSource || !PreferProxy</c> — which, given the invariant, is equivalent to
/// <c>!PreferProxy</c>.
/// </para>
/// <para>
/// <b>Why these properties are <c>set</c>, not <c>init</c>.</b>
/// <see cref="ForceOriginalSource"/>, <see cref="PreferProxy"/>, and
/// <see cref="DisableResourceShare"/> are <c>{ get; set; }</c> rather than <c>{ get; init; }</c>
/// because node-graph render nodes (NodeGraphFilterEffectRenderNode, GraphSnapshot) reuse a
/// cached <see cref="CompositionContext"/> and mutate these fields per evaluation instead of
/// allocating a fresh one — an allocation-avoidance measure on the render hot path. Restoring
/// <c>init</c> would break that replay.
/// </para>
/// <para>
/// <see cref="PreferredProxyPreset"/> is the resolve-time density cap passed to
/// <see cref="ProxyResolver.Resolve(Uri, ProxyPreset)"/>, which prefers the densest Ready proxy
/// whose scale does not exceed this preset's scale.
/// </para>
/// </remarks>
public class CompositionContext(TimeSpan time)
{
    public static CompositionContext Default { get; } = new(TimeSpan.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    public TimeSpan Time { get; set; } = time;

    public bool DisableResourceShare { get; set; }

    public bool ForceOriginalSource { get; set; }

    public bool PreferProxy { get; set; }

    public ProxyPreset PreferredProxyPreset { get; set; } = ProxyPreset.Quarter;

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }
}
