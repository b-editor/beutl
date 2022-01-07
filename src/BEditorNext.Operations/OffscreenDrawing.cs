using BEditorNext.Graphics;
using BEditorNext.Graphics.Transformation;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class OffscreenDrawing : ConfigureOperation<IDrawable>
{
    public override void Configure(in OperationRenderArgs args, ref IDrawable obj)
    {
        static Point CreateRelPoint(Size size, IDrawable obj)
        {
            float x = 0;
            float y = 0;

            if (obj.HorizontalContentAlignment == AlignmentX.Center)
            {
                x -= size.Width / 2;
            }
            else if (obj.HorizontalContentAlignment == AlignmentX.Right)
            {
                x -= size.Width;
            }

            if (obj.VerticalContentAlignment == AlignmentY.Center)
            {
                y -= size.Height / 2;
            }
            else if (obj.VerticalContentAlignment == AlignmentY.Bottom)
            {
                y -= size.Height;
            }

            return new Point(x, y);
        }

        IBitmap bmp = obj.ToBitmap();
        var srcRect = new Rect(obj.Size.ToSize(1));
        Rect bounds = Graphics.Effects.BitmapEffect.MeasureAll(srcRect, obj.Effects);
        var result = new DrawableBitmap(bmp)
        {
            BlendMode = obj.BlendMode,
            Foreground = obj.Foreground,
            HorizontalAlignment = obj.HorizontalAlignment,
            IsAntialias = obj.IsAntialias,
            VerticalAlignment = obj.VerticalAlignment,
        };

        result.Transform.Add(new TranslateTransform(bounds.Position));
        result.Transform.AddRange(obj.Transform);
        result.Transform.Add(new TranslateTransform(CreateRelPoint(srcRect.Size, obj)));

        obj = result;
    }
}
