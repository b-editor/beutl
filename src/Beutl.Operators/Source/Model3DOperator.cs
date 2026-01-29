using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Models;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Model3D), ResourceType = typeof(Strings))]
public sealed class Model3DOperator : PublishOperator<Model3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Source);
        AddProperty(Value.Children);
        AddProperty(Value.Position);
        AddProperty(Value.Rotation);
        AddProperty(Value.Scale);
        AddProperty(Value.CastShadows);
        AddProperty(Value.ReceiveShadows);
    }
}
