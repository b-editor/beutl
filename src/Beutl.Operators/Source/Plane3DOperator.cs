using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Plane3D), ResourceType = typeof(Strings))]
public sealed class Plane3DOperator : PublishOperator<Plane3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Width);
        AddProperty(Value.Height);
        AddProperty(Value.WidthSegments);
        AddProperty(Value.HeightSegments);
        AddProperty(Value.Position);
        AddProperty(Value.Rotation);
        AddProperty(Value.Scale);
        AddProperty(Value.Material);
        AddProperty(Value.CastShadows);
        AddProperty(Value.ReceiveShadows);
    }
}
