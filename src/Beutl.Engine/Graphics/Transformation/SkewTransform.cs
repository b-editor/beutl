using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(Strings.Skew), ResourceType = typeof(Strings))]
public sealed class SkewTransform : Transform
{
    public SkewTransform(float skewX, float skewY) : this()
    {
        SkewX.CurrentValue = skewX;
        SkewY.CurrentValue = skewY;
    }

    public SkewTransform()
    {
        ScanProperties<SkewTransform>();
    }

    [Display(Name = nameof(Strings.SkewX), ResourceType = typeof(Strings))]
    public IProperty<float> SkewX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.SkewY), ResourceType = typeof(Strings))]
    public IProperty<float> SkewY { get; } = Property.CreateAnimatable<float>();

    public override Matrix CreateMatrix(RenderContext context)
    {
        float skewX = context.Get(SkewX);
        float skewY = context.Get(SkewY);
        return Matrix.CreateSkew(MathUtilities.Deg2Rad(skewX), MathUtilities.Deg2Rad(skewY));
    }

    public static SkewTransform FromRadians(float skewX, float skewY)
    {
        return new SkewTransform(MathUtilities.Rad2Deg(skewX), MathUtilities.Rad2Deg(skewY));
    }
}
