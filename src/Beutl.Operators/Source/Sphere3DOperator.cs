using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Sphere3D), ResourceType = typeof(Strings))]
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
