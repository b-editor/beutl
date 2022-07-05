using System.Reactive.Linq;

using BeUtl.Graphics.Transformation;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.Transform;

public sealed class Rotation3DOperator : TransformOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Rotation3DTransform>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.RotationXProperty)
        {
            Header = Observable.Return("X"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.RotationYProperty)
        {
            Header = Observable.Return("Y"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.RotationZProperty)
        {
            Header = Observable.Return("Z"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.CenterXProperty)
        {
            Header = Observable.Return("Center x"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.CenterYProperty)
        {
            Header = Observable.Return("Center y"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.CenterZProperty)
        {
            Header = Observable.Return("Center z"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
        initializing.Add(new SetterDescription<float>(Rotation3DTransform.DepthProperty)
        {
            Header = Observable.Return("Depth"),
            DefaultValue = 0,
            IsAnimatable = true,
        });
    }
}
