using System.Numerics;

using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media.Pixel;
using BEditorNext.Rendering;

namespace BEditorNext.Media.TextFormatting;

public class FormattedText : IRenderableBitmap
{
    private readonly List<TextLine> _lines;
    private readonly IList<BitmapEffect> _effects = new List<BitmapEffect>();

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

    public Dictionary<string, object> Options { get; } = new();

    PixelSize IRenderableBitmap.Size
    {
        get
        {
            Size size = Bounds;
            return new PixelSize((int)size.Width, (int)size.Height);
        }
    }

    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

    public (AlignmentX X, AlignmentY Y) Alignment { get; set; }

    IList<BitmapEffect> IRenderableBitmap.Effects => _effects;

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
        IGraphics graphics = renderer.Graphics;
        Render(graphics);
    }

    public void Render(IGraphics graphics)
    {
        if (_effects.Count == 0)
        {
            RenderDirect(graphics);
        }
        else
        {
            RenderBitmap(graphics);
        }
    }

    private void RenderBitmap(IGraphics graphics)
    {
        using Bitmap<Bgra8888> bitmap = ToBitmap();
        using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, _effects);

        graphics.PushMatrix();
        graphics.SetMatrix(Transform * graphics.TotalMatrix);
        Size size = Bounds;
        Point pt = CreatePoint(size.Width, size.Height);
        graphics.Translate(pt);
        graphics.DrawBitmap(bitmap2);

        graphics.PopMatrix();
    }

    private void RenderDirect(IGraphics graphics)
    {
        graphics.PushMatrix();
        graphics.SetMatrix(Transform * graphics.TotalMatrix);
        Size size = Bounds;
        Point pt = CreatePoint(size.Width, size.Height);
        graphics.Translate(pt);
        RenderCore(graphics);

        graphics.PopMatrix();
    }

    private void RenderCore(IGraphics graphics)
    {
        graphics.PushMatrix();

        float prevBottom = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            TextLine line = Lines[i];
            Size lineBounds = line.Measure();
            float ascent = line.MinAscent();

            graphics.PushMatrix();
            graphics.Translate(new(0, prevBottom - ascent));

            float prevRight = 0;
            foreach (TextElement element in line.Elements)
            {
                graphics.Translate(new(prevRight + element.Margin.Left, 0));
                Size elementBounds = element.Measure();

                graphics.PushMatrix();
                graphics.Translate(new(0, element.Margin.Top));
                graphics.DrawText(element);
                graphics.PopMatrix();

                prevRight = elementBounds.Width + element.Margin.Right;
            }

            prevBottom += lineBounds.Height;
            graphics.PopMatrix();
        }

        graphics.PopMatrix();
    }

    private Point CreatePoint(float width, float height)
    {
        float x = 0;
        float y = 0;

        if (Alignment.X == AlignmentX.Center)
        {
            x -= width / 2;
        }
        else if (Alignment.X == AlignmentX.Right)
        {
            x -= width;
        }

        if (Alignment.Y == AlignmentY.Center)
        {
            y -= height / 2;
        }
        else if (Alignment.Y == AlignmentY.Bottom)
        {
            y -= height;
        }

        return new Point(x, y);
    }

    public Bitmap<Bgra8888> ToBitmap()
    {
        Size size = Bounds;
        using var g = new Graphics.Graphics((int)size.Width, (int)size.Height);

        RenderCore(g);

        return g.GetBitmap();
    }
}
