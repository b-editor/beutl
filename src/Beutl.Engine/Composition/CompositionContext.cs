using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

/// <summary>
/// Per-evaluation context flowing through the composition graph. Carries the proxy/original
/// decode selection, the resource-sharing toggle, the resolve-time proxy density cap, and the ambient
/// render policy for nested evaluation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Proxy/original selection.</b> A single boolean, <see cref="PreferProxy"/>, is the only
/// decode-selection signal consumers read. The producer (SceneCompositor's nested
/// CompositorContext) seeds it from the top-level "force original" input and the user-facing
/// <c>EditorConfig.PreviewSourceMode</c>:
/// <c>PreferProxy = !forceOriginal &amp;&amp; EditorConfig.PreviewSourceMode == PreferProxy</c>.
/// A nested scene propagates "decode original" simply as <c>!PreferProxy</c>. There is no separate
/// force-original bit on the context to keep in sync.
/// </para>
/// <para>
/// <b>Why these properties are <c>set</c>, not <c>init</c>.</b>
/// <see cref="PreferProxy"/> and <see cref="DisableResourceShare"/> are <c>{ get; set; }</c>
/// rather than <c>{ get; init; }</c> because node-graph render nodes
/// (NodeGraphFilterEffectRenderNode, GraphSnapshot) reuse a cached <see cref="CompositionContext"/>
/// and mutate these fields per evaluation instead of allocating a fresh one — an
/// allocation-avoidance measure on the render hot path. Restoring <c>init</c> would break that replay.
/// </para>
/// <para>
/// <see cref="PreferredProxyPreset"/> is the resolve-time density cap passed to
/// <see cref="ProxyResolver.Resolve(Uri, ProxyPreset)"/>, which prefers the densest Ready proxy
/// whose scale does not exceed this preset's scale.
/// </para>
/// <para>
/// <b>Render policy.</b> Property and animation evaluation also use this type, so ordinary callers may
/// omit the policy and receive the meaningful non-delivery default <see cref="Graphics.Rendering.RenderIntent.Preview"/>
/// with <see cref="RenderPullPurpose.Frame"/>. Render hosts pass both values explicitly. The values are
/// publicly observable by nested resources and plugin nodes but cannot be changed by external consumers.
/// </para>
/// </remarks>
public class CompositionContext
{
    /// <summary>
    /// Initializes ordinary property or animation evaluation with preview/frame policy.
    /// </summary>
    public CompositionContext(TimeSpan time)
        : this(time, RenderIntent.Preview, RenderPullPurpose.Frame)
    {
    }

    /// <summary>Initializes evaluation with an explicit ambient render policy.</summary>
    public CompositionContext(
        TimeSpan time,
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        Time = time;
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
    }

    // A fresh instance per access, not a shared singleton: Time / DisableResourceShare / PreferProxy /
    // PreferredProxyPreset are all mutable, so a caller that writes to a context it received must not be
    // able to corrupt a global baseline for every other render.
    public static CompositionContext Default => new(TimeSpan.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    public TimeSpan Time { get; set; }

    public bool DisableResourceShare { get; set; }

    public bool PreferProxy { get; set; }

    public ProxyPreset PreferredProxyPreset { get; set; } = ProxyPreset.Quarter;

    /// <summary>The ambient preview or delivery failure policy for nested evaluation.</summary>
    public RenderIntent RenderIntent { get; private set; }

    /// <summary>Whether nested evaluation belongs to a normal frame pull or auxiliary work.</summary>
    public RenderPullPurpose PullPurpose { get; private set; }

    internal void UpdateRenderPolicy(RenderIntent renderIntent, RenderPullPurpose pullPurpose)
    {
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
    }

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }
}
