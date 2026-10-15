using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Records its inputs inside a finite layer over <paramref name="domain"/>, or inside a full-target layer
/// scope when the domain is empty.
/// </summary>
internal sealed class TestLayerRenderNode(Rect domain = default) : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
        => context.Publish(domain == default
            ? context.TargetLayerScope(context.Inputs, TargetRegion.Full)
            : context.Layer(context.Inputs, domain));
}
