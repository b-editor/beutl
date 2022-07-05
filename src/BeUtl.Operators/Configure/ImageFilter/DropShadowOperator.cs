using BeUtl.Graphics;
using BeUtl.Graphics.Filters;
using BeUtl.Language;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.ImageFilter;

public sealed class DropShadowOperator : ImageFilterOperator
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<DropShadow>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<Point>(DropShadow.PositionProperty)
        {
            DefaultValue = new Point(10, 10),
            Header = StringResources.Common.PositionObservable,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<Vector>(DropShadow.SigmaProperty)
        {
            DefaultValue = new Vector(10, 10),
            Minimum = Vector.Zero,
            Header = StringResources.Common.SigmaObservable,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<Color>(DropShadow.ColorProperty)
        {
            DefaultValue = Colors.Black,
            Header = StringResources.Common.ColorObservable,
            IsAnimatable = true
        });
        initializing.Add(new SetterDescription<bool>(DropShadow.ShadowOnlyProperty)
        {
            DefaultValue = false,
            Header = StringResources.Common.ColorObservable,
            IsAnimatable = true
        });
    }
}
