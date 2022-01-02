using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Media.TextFormatting;

public class FormattedText : Drawable
{
    private readonly List<TextLine> _lines;

    public FormattedText()
        : this(new List<TextLine>())
    {

    }

    private FormattedText(List<TextLine> lines)
    {
        _lines = lines;
    }

    ~FormattedText()
    {
        Dispose();
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

    public override PixelSize Size
    {
        get
        {
            Size size = Bounds;
            return new PixelSize((int)size.Width, (int)size.Height);
        }
    }

    public static FormattedText Parse(string s, FormattedTextInfo info)
    {
        var tokenizer = new FormattedTextTokenizer(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        return new FormattedText(lines);
    }

    public void Initialize(string s, FormattedTextInfo info)
    {
        Initialize();

        var tokenizer = new FormattedTextTokenizer(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        _lines.AddRange(lines);
    }

    public override void Dispose()
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

    public Bitmap<Bgra8888> ToBitmapWithoutEffect()
    {
        Size size = Bounds;
        using var g = new Canvas((int)size.Width, (int)size.Height);

        DrawCore(g);

        return g.GetBitmap();
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (Effects.Count == 0)
        {
            DrawCore(canvas);
        }
        else
        {
            DrawBitmap(canvas);
        }
    }

    protected override void OnInitialize()
    {
        Lines.Clear();
        GC.ReRegisterForFinalize(this);
        IsDisposed = false;
    }

    private void DrawBitmap(ICanvas canvas)
    {
        using Bitmap<Bgra8888> bitmap = ToBitmapWithoutEffect();
        using Bitmap<Bgra8888> bitmap2 = BitmapEffect.ApplyAll(bitmap, Effects);

        canvas.DrawBitmap(bitmap2);
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
}
