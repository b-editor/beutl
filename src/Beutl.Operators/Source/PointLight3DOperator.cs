using Beutl.Graphics3D.Lighting;
using Beutl.Operation;

namespace Beutl.Operators.Source;

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
