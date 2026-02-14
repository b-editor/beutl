using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[SuppressResourceClassGeneration]
[Display(Name = nameof(Strings.Presenter), ResourceType = typeof(Strings))]
public sealed class TransformPresenter : Transform, IPresenter<Transform>
{
    public TransformPresenter()
    {
        ScanProperties<TransformPresenter>();
    }

    [Display(Name = nameof(Strings.Target), ResourceType = typeof(Strings))]
    public IProperty<Transform?> Target { get; } = Property.Create<Transform?>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        var target = context.Get(Target);
        return target?.CreateMatrix(context) ?? Matrix.Identity;
    }
}
