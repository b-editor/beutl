using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

[Display(Name = nameof(GraphicsStrings.RotationTransform), ResourceType = typeof(GraphicsStrings))]
public sealed class RotationTransform : Transform
{
    public RotationTransform()
    {
        ScanProperties<RotationTransform>();
    }

    public RotationTransform(float rotation) : this()
    {
        Rotation.CurrentValue = rotation;
    }

    [Display(Name = nameof(GraphicsStrings.RotationTransform_Rotation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Rotation { get; } = Property.CreateAnimatable<float>();

    public override Matrix CreateMatrix(CompositionContext context)
    {
        float rot = context.Get(Rotation);
        return Matrix.CreateRotation(MathUtilities.Deg2Rad(rot));
    }

    public static RotationTransform FromRadians(float radians)
    {
        return new RotationTransform(MathUtilities.Rad2Deg(radians));
    }
}
