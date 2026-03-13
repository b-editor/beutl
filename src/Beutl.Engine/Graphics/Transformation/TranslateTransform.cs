using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.TranslateTransform), ResourceType = typeof(GraphicsStrings))]
public sealed class TranslateTransform : Transform
{
    public TranslateTransform()
    {
        ScanProperties<TranslateTransform>();
    }

    public TranslateTransform(float x, float y) : this()
    {
        X.CurrentValue = x;
        Y.CurrentValue = y;
    }

    public TranslateTransform(Vector vector) : this()
    {
        X.CurrentValue = vector.X;
        Y.CurrentValue = vector.Y;
    }

    public TranslateTransform(Point point) : this()
    {
        X.CurrentValue = point.X;
        Y.CurrentValue = point.Y;
    }

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_X), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> X { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_Y), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Y { get; } = Property.CreateAnimatable(0f);

    public override Matrix CreateMatrix(CompositionContext context)
    {
        float x = context.Get(X);
        float y = context.Get(Y);
        return Matrix.CreateTranslation(x, y);
    }
}
