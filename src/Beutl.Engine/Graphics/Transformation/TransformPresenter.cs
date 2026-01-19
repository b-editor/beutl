using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
public sealed class TransformPresenter : Transform
{
    public TransformPresenter()
    {
        ScanProperties<TransformPresenter>();
    }

    public IProperty<Reference<Transform>> Target { get; } = Property.Create<Reference<Transform>>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        var target = context.Get(Target).Value;
        return target?.CreateMatrix(context) ?? Matrix.Identity;
    }
}
