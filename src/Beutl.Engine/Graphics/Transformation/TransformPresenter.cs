using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
public sealed class TransformPresenter : Transform, IPresenter<Transform>
{
    public TransformPresenter()
    {
        ScanProperties<TransformPresenter>();
    }

    public IProperty<Transform?> Target { get; } = Property.Create<Transform?>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        var target = context.Get(Target);
        return target?.CreateMatrix(context) ?? Matrix.Identity;
    }
}
