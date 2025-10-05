using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public abstract partial class FilterEffect<TNode, TOptions> : FilterEffect
    where TNode : FilterEffectRenderNode<TOptions>
    where TOptions : struct, IEquatable<TOptions>
{
    public abstract TNode CreateNode(TOptions options);

    public abstract TOptions CreateOptions(RenderContext context);
}

[DummyType(typeof(DummyFilterEffect))]
public abstract partial class FilterEffect : EngineObject, IAffectsRender
{
}
