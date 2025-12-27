using Beutl.Graphics3D.Lighting;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class DirectionalLight3DOperator : PublishOperator<DirectionalLight3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Direction);
        AddProperty(Value.ShadowDistance);
        AddProperty(Value.ShadowMapSize);
        AddProperty(Value.Color);
        AddProperty(Value.Intensity);
        AddProperty(Value.CastsShadow);
        AddProperty(Value.ShadowBias);
        AddProperty(Value.ShadowNormalBias);
        AddProperty(Value.ShadowStrength);
    }
}
