using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media.Pixel;
using BEditorNext.Rendering;

namespace BEditorNext.Media.TextFormatting;

public class FormattedText : IRenderableBitmap
{
    private readonly List<TextLine> _lines;
    private readonly List<BitmapEffect> _effects = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public FormattedText()
        : this(new List<TextLine>())
    {

    }

    private FormattedText(List<TextLine> lines)
    {
        _lines = lines;
    }

    public IList<TextLine> Lines => _lines;

    public Size Bounds
    {
        get
        {
            float width = 0;
            float height = 0;

            foreach (TextLine line in Lines)
            {
                Size bounds = line.Measure();
                width = MathF.Max(bounds.Width, width);
                height += bounds.Height;
            }

            return new Size(width, height);
        }
    }

    public bool IsDisposed { get; private set; }

    public ref Matrix3x2 Transform => ref _transform;

    PixelSize IDrawable.Size
    {
        get
        {
            Size size = Bounds;
            return new PixelSize((int)size.Width, (int)size.Height);
        }
    }

    IList<BitmapEffect> IDrawable.Effects => _effects;

    AlignmentX IDrawable.HorizontalAlignment { get; set; }

    AlignmentY IDrawable.VerticalAlignment { get; set; }

    AlignmentX IDrawable.HorizontalContentAlignment { get; set; }

    AlignmentY IDrawable.VerticalContentAlignment { get; set; }

    public static FormattedText Parse(string s, FormattedTextInfo info)
    {
        var tokenizer = new FormattedTextTokenizer(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        return new FormattedText(lines);
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            foreach (TextLine item in Lines)
            {
                item.Dispose();
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public void Render(IRenderer renderer)
    {
        Draw(renderer.Graphics);
    }

    public void Draw(ICanvas canvas)
    {
        if (_effects.Count == 0)
        {
            DrawDirect(canvas);
        }
        else
        {
            DrawBitmap(canvas);
        }
    }

    private void DrawBitmap(ICanvas canvas)
    {
        using Bitmap<Bgra8888> bitmap = ToBitmap();
        using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, _effects);
        Rect rect = BitmapEffect.MeasureAll(new Rect(Bounds), _effects);

        canvas.PushMatrix();
        canvas.SetMatrix(Transform * canvas.TotalMatrix);

        Point pt = CreatePoint(Bounds, canvas.Size) + rect.Position;
        canvas.Translate(pt);

        canvas.DrawBitmap(bitmap2);

        canvas.PopMatrix();
    }

    private void DrawDirect(ICanvas canvas)
    {
        canvas.PushMatrix();
        canvas.SetMatrix(Transform * canvas.TotalMatrix);

        Point pt = CreatePoint(Bounds, canvas.Size);
        canvas.Translate(pt);

        DrawCore(canvas);

        canvas.PopMatrix();
    }

    private void DrawCore(ICanvas canvas)
    {
        canvas.PushMatrix();

        float prevBottom = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            TextLine line = Lines[i];
            Size lineBounds = line.Measure();
            float ascent = line.MinAscent();

            canvas.PushMatrix();
            canvas.Translate(new(0, prevBottom - ascent));

            float prevRight = 0;
            foreach (TextElement element in line.Elements)
            {
                canvas.Translate(new(prevRight + element.Margin.Left, 0));
                Size elementBounds = element.Measure();

                canvas.PushMatrix();
                canvas.Translate(new(0, element.Margin.Top));
                canvas.DrawText(element);
                canvas.PopMatrix();

                prevRight = elementBounds.Width + element.Margin.Right;
            }

            prevBottom += lineBounds.Height;
            canvas.PopMatrix();
        }

        canvas.PopMatrix();
    }


    public Bitmap<Bgra8888> ToBitmap()
    {
        Size size = Bounds;
        using var g = new Graphics.Canvas((int)size.Width, (int)size.Height);

        DrawCore(g);

        return g.GetBitmap();
    }

    private Point CreatePoint(Size size, PixelSize canvasSize)
    {
        var drawable = this as IDrawable;
        float x = 0;
        float y = 0;

        if (drawable.HorizontalContentAlignment == AlignmentX.Center)
        {
            x -= size.Width / 2;
        }
        else if (drawable.HorizontalContentAlignment == AlignmentX.Right)
        {
            x -= size.Width;
        }

        if (drawable.VerticalContentAlignment == AlignmentY.Center)
        {
            y -= size.Height / 2;
        }
        else if (drawable.VerticalContentAlignment == AlignmentY.Bottom)
        {
            y -= size.Height;
        }

        if (drawable.HorizontalAlignment == AlignmentX.Center)
        {
            x += canvasSize.Width / 2;
        }
        else if (drawable.HorizontalAlignment == AlignmentX.Right)
        {
            x += canvasSize.Width;
        }

        if (drawable.VerticalAlignment == AlignmentY.Center)
        {
            y += canvasSize.Height / 2;
        }
        else if (drawable.VerticalAlignment == AlignmentY.Bottom)
        {
            y += canvasSize.Height;
        }

        return new Point(x, y);
    }
}
