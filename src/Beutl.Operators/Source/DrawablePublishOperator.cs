using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Filters;
using Beutl.Graphics.Transformation;
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

    protected override void OnPostPublish()
    {
        base.OnPostPublish();
        if (Instance?.Target is T drawable)
        {
            drawable.BlendMode = BlendMode.SrcOver;
            drawable.AlignmentX = AlignmentX.Left;
            drawable.AlignmentY = AlignmentY.Top;
            drawable.TransformOrigin = RelativePoint.TopLeft;
            if (drawable.Transform is TransformGroup transformGroup)
                transformGroup.Children.Clear();
            else
                drawable.Transform = new TransformGroup();

            if (drawable.Filter is ImageFilterGroup filterGroup)
                filterGroup.Children.Clear();
            else
                drawable.Filter = new ImageFilterGroup();

            if (drawable.Effect is BitmapEffectGroup effectGroup)
                effectGroup.Children.Clear();
            else
                drawable.Effect = new BitmapEffectGroup();
        }
    }
}
