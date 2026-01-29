using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Primitives;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Cube3D), ResourceType = typeof(Strings))]
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
