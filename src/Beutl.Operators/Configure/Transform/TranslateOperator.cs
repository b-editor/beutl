using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Transform;

public sealed class TranslateOperator : TransformOperator<TranslateTransform>
{
    public Setter<float> X { get; set; } = new(TranslateTransform.XProperty);

    public Setter<float> Y { get; set; } = new(TranslateTransform.YProperty);
}
