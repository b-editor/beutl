using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Effects;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Language;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Serialization;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Scene3D), ResourceType = typeof(Strings))]
public sealed class Scene3DOperator : PublishOperator<Scene3D>
{
    protected override void FillProperties()
    {
        AddProperty(Value.Camera, new PerspectiveCamera());
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

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (!IsEnabled)
        {
            Value.Lights.Clear();
            Value.Objects.Clear();
            return;
        }

        var lights = new List<Light3D>(context.FlowRenderables.Count);
        var objects = new List<Object3D>(context.FlowRenderables.Count);
        for (int i = context.FlowRenderables.Count - 1; i >= 0; i--)
        {
            switch (context.FlowRenderables[i])
            {
                case Light3D light:
                    context.FlowRenderables.RemoveAt(i);
                    lights.Insert(0, light);
                    break;
                case Object3D obj:
                    context.FlowRenderables.RemoveAt(i);
                    // NOTE: 順番を気にしないのであれば，Addでも良い
                    // オブジェクトやライトの数が多い場合はInsert(0, ...)の方が遅くなる可能性があるので注意
                    objects.Insert(0, obj);
                    break;
            }
        }

        Value.Lights.Replace(lights);
        Value.Objects.Replace(objects);
        base.Evaluate(context);
    }

    public override void Enter()
    {
        base.Enter();
        Value.Lights.Clear();
        Value.Objects.Clear();
    }

    public override void Exit()
    {
        base.Exit();
        Value.Lights.Clear();
        Value.Objects.Clear();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        Value.Lights.Clear();
        Value.Objects.Clear();
        base.Serialize(context);
    }
}
