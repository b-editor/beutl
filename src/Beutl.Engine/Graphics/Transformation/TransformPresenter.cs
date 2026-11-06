using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed partial class TransformPresenter : Transform, IPresenter<Transform>
{
    public TransformPresenter()
    {
        ScanProperties<TransformPresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Transform?> Target { get; } = Property.CreateReference<Transform?>();

    // A disabled target applies nothing here either, as a drawable's or a filter effect's presenter draws nothing for one.
    public override Matrix CreateMatrix(CompositionContext context)
    {
        var target = context.Get(Target);
        return target != null && target.IsEnabled.Value ? target.CreateMatrix(context) : Matrix.Identity;
    }
}
