using BeUtl.Graphics;
using BeUtl.Language;
using BeUtl.Media;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure;

public sealed class AlignmentOperator : StreamStyler
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Drawable>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
        initializing.Add(new SetterDescription<AlignmentX>(Drawable.CanvasAlignmentXProperty)
        {
            Header = StringResources.Common.CanvasAlignmentXObservable
        });
        initializing.Add(new SetterDescription<AlignmentY>(Drawable.CanvasAlignmentYProperty)
        {
            Header = StringResources.Common.CanvasAlignmentYObservable
        });
        initializing.Add(new SetterDescription<AlignmentX>(Drawable.AlignmentXProperty)
        {
            Header = StringResources.Common.AlignmentXObservable
        });
        initializing.Add(new SetterDescription<AlignmentY>(Drawable.AlignmentYProperty)
        {
            Header = StringResources.Common.AlignmentYObservable
        });
    }
}
