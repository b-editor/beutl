using System.Reactive.Linq;

using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure;

public sealed class OpacityMaskOperator : StreamStyler
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<IBrush?>(Drawable.OpacityMaskProperty)
        {
            Header = Observable.Return("不透明マスク"),
        });
    }
}
