using System.Reactive.Linq;

using BeUtl.Graphics;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure;

public sealed class BlendOperator : StreamStyler
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<BlendMode>(Drawable.BlendModeProperty)
        {
            Header = Observable.Return("合成モード")
        });
    }
}
