using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings))]
public sealed class ScaleTransform : Transform
{
    public ScaleTransform()
    {
        ScanProperties<ScaleTransform>();
    }

    public ScaleTransform(Vector vector, float scale = 100) : this()
    {
        Scale.CurrentValue = scale;
        ScaleX.CurrentValue = vector.X;
        ScaleY.CurrentValue = vector.Y;
    }

    public ScaleTransform(float x, float y, float scale = 100) : this()
    {
        Scale.CurrentValue = scale;
        ScaleX.CurrentValue = x;
        ScaleY.CurrentValue = y;
    }

    [Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings))]
    public IProperty<float> Scale { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.ScaleX), ResourceType = typeof(Strings))]
    public IProperty<float> ScaleX { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(Strings.ScaleY), ResourceType = typeof(Strings))]
    public IProperty<float> ScaleY { get; } = Property.CreateAnimatable(100f);

    public override Matrix CreateMatrix(RenderContext context)
    {
        float scale = context.Get(Scale) / 100f;
        float scaleX = context.Get(ScaleX) / 100f;
        float scaleY = context.Get(ScaleY) / 100f;
        return Matrix.CreateScale(scale * scaleX, scale * scaleY);
    }
}
