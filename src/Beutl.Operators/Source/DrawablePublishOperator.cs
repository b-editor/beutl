using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Filters;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public abstract class DrawablePublishOperator<T> : StyledSourcePublisher
    where T : Drawable
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<T>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnBeforeApplying()
    {
        base.OnBeforeApplying();
        if (Instance?.Target is T drawable)
        {
            drawable.BeginBatchUpdate();
        }
    }

    protected override void OnAfterApplying()
    {
        base.OnAfterApplying();
        if (Instance?.Target is T drawable)
        {
            drawable.BlendMode = BlendMode.SrcOver;
            //drawable.AlignmentX = AlignmentX.Left;
            //drawable.AlignmentY = AlignmentY.Top;
            //drawable.TransformOrigin = RelativePoint.TopLeft;
            //drawable.Transform = null;
            //drawable.Filter = null;
            drawable.Effect = null;
        }
    }
}
