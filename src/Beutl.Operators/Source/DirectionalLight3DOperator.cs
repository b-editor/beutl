using System.ComponentModel.DataAnnotations;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Operation;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.DirectionalLight3D), ResourceType = typeof(Strings))]
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
