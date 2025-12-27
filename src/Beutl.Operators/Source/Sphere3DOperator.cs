using Beutl.Graphics3D.Primitives;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class Sphere3DOperator : PublishOperator<Sphere3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Radius);
        AddProperty(Value.Segments);
        AddProperty(Value.Rings);
        AddProperty(Value.Position);
        AddProperty(Value.Rotation);
        AddProperty(Value.Scale);
        AddProperty(Value.Material);
        AddProperty(Value.CastShadows);
        AddProperty(Value.ReceiveShadows);
    }
}
