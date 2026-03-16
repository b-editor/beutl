using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.SkewTransform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SkewTransform : Transform
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

    [Display(Name = nameof(GraphicsStrings.SkewTransform_SkewX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> SkewX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.SkewTransform_SkewY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> SkewY { get; } = Property.CreateAnimatable<float>();

    public override Matrix CreateMatrix(CompositionContext context)
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
