using Beutl.Graphics3D.Lighting;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SpotLight3DOperator : PublishOperator<SpotLight3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Position);
        AddProperty(Value.Direction);
        AddProperty(Value.InnerConeAngle);
        AddProperty(Value.OuterConeAngle);
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
