using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.PointLight3D), ResourceType = typeof(Strings))]
public sealed class PointLight3DOperator : PublishOperator<PointLight3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Position);
        AddProperty(Value.ConstantAttenuation);
        AddProperty(Value.LinearAttenuation);
        AddProperty(Value.QuadraticAttenuation);
        AddProperty(Value.Range);
        AddProperty(Value.Color);
        AddProperty(Value.Intensity);
        AddProperty(Value.CastsShadow);
        AddProperty(Value.ShadowBias);
        AddProperty(Value.ShadowNormalBias);
        AddProperty(Value.ShadowStrength);
    }
}
