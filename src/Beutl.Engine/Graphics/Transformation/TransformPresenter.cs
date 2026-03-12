using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed class TransformPresenter : Transform, IPresenter<Transform>
{
    public TransformPresenter()
    {
        ScanProperties<TransformPresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Transform?> Target { get; } = Property.Create<Transform?>();

    public override Matrix CreateMatrix(CompositionContext context)
    {
        var target = context.Get(Target);
        return target?.CreateMatrix(context) ?? Matrix.Identity;
    }
}
