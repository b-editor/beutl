using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Transform;

public sealed class Rotation3DOperator : TransformOperator<Rotation3DTransform>
{
    public Setter<float> RotationX { get; set; } = new(Rotation3DTransform.RotationXProperty);

    public Setter<float> RotationY { get; set; } = new(Rotation3DTransform.RotationYProperty);

    public Setter<float> RotationZ { get; set; } = new(Rotation3DTransform.RotationZProperty);

    public Setter<float> CenterX { get; set; } = new(Rotation3DTransform.CenterXProperty);

    public Setter<float> CenterY { get; set; } = new(Rotation3DTransform.CenterYProperty);

    public Setter<float> CenterZ { get; set; } = new(Rotation3DTransform.CenterZProperty);

    public Setter<float> Depth { get; set; } = new(Rotation3DTransform.DepthProperty);
}
