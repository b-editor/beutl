using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(Strings.Translate), ResourceType = typeof(Strings))]
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

    public IProperty<float> X { get; } = Property.CreateAnimatable(0f);

    public IProperty<float> Y { get; } = Property.CreateAnimatable(0f);

    public override Matrix CreateMatrix(RenderContext context)
    {
        float x = context.Get(X);
        float y = context.Get(Y);
        return Matrix.CreateTranslation(x, y);
    }
}
