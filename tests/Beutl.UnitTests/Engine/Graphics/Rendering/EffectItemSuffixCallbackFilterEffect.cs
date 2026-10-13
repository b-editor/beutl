using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>A filter effect whose <see cref="ApplyTo"/> is the delegate a test passes in.</summary>
[SuppressResourceClassGeneration]
internal sealed partial class EffectItemSuffixCallbackFilterEffect(
    Action<FilterEffectContext, FilterEffect.Resource> apply) : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => apply(context, resource);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
