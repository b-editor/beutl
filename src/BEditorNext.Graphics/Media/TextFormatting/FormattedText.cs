using BEditorNext.Graphics;
using BEditorNext.Rendering;

namespace BEditorNext.Media.TextFormatting;

public class FormattedText : IRenderable
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
}
