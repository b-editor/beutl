using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Transform;

public sealed class ScaleOperator : TransformOperator<ScaleTransform>
{
    public Setter<float> Scale { get; set; } = new(ScaleTransform.ScaleProperty);

    public Setter<float> ScaleX{get;set;}=new(ScaleTransform.ScaleXProperty);

    public Setter<float> ScaleY{ get;set; } =new(ScaleTransform.ScaleYProperty);
}
