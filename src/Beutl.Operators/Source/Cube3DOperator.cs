using Beutl.Graphics3D.Primitives;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class Cube3DOperator : PublishOperator<Cube3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Width);
        AddProperty(Value.Height);
        AddProperty(Value.Depth);
        AddProperty(Value.Position);
        AddProperty(Value.Rotation);
        AddProperty(Value.Scale);
        AddProperty(Value.Material);
        AddProperty(Value.CastShadows);
        AddProperty(Value.ReceiveShadows);
    }
}
