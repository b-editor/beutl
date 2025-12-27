using Beutl.Graphics.Effects;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Media;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class Scene3DOperator : PublishOperator<Scene3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Camera, new PerspectiveCamera());
        AddProperty(Value.Objects);
        AddProperty(Value.Lights);
        AddProperty(Value.AmbientColor, Colors.White);
        AddProperty(Value.AmbientIntensity, 0.1f);
        AddProperty(Value.RenderWidth, 1920f);
        AddProperty(Value.RenderHeight, 1080f);
        AddProperty(Value.BackgroundColor, Colors.Black);
        AddProperty(Value.Fill, new SolidColorBrush(Colors.White));
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }
}
