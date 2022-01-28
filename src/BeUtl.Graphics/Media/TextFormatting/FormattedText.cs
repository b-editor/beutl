using BeUtl.Graphics;

namespace BeUtl.Media.TextFormatting;

public sealed class TextLines : AffectsRenders<TextLine>
{

}

public class FormattedText : Drawable
{
    public static readonly CoreProperty<TextLines> LinesProperty;
    private readonly TextLines _lines;

    static FormattedText()
    {
        LinesProperty = ConfigureProperty<TextLines, FormattedText>(nameof(Lines))
            .Accessor(o => o.Lines, (o, v) => o.Lines = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();
    }

    public FormattedText()
    {
        _lines = new()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _lines.Invalidated += (_, _) => Invalidate();
    }

    private FormattedText(List<TextLine> lines)
        : this()
    {
        _lines.AddRange(lines);
    }

    ~FormattedText()
    {
        Dispose();
    }

    public TextLines Lines
    {
        get => _lines;
        set => _lines.Replace(value);
    }

    public static FormattedText Parse(string s, FormattedTextInfo info)
    {
        var tokenizer = new FormattedTextParser(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        return new FormattedText(lines);
    }

    public void Load(string s, FormattedTextInfo info)
    {
        IsDisposed = false;
        var tokenizer = new FormattedTextParser(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        _lines.Replace(lines);
        Invalidate();
    }

    public void Initialize(string s, FormattedTextInfo info)
    {
        Initialize();

        var tokenizer = new FormattedTextParser(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        _lines.AddRange(lines);
        Invalidate();
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

    protected override void OnDraw(ICanvas canvas)
    {
        DrawCore(canvas);
    }

    protected override void OnInitialize()
    {
        Lines.Clear();
        GC.ReRegisterForFinalize(this);
        IsDisposed = false;
    }

    private void DrawCore(ICanvas canvas)
    {
        float prevBottom = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            TextLine line = Lines[i];
            line.Measure(canvas.Size.ToSize(1));
            Rect lineBounds = line.Bounds;

            using (canvas.PushTransform(Matrix.CreateTranslation(0, prevBottom)))
            {
                line.Draw(canvas);
                prevBottom += lineBounds.Height;
            }
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        float width = 0;
        float height = 0;

        foreach (TextLine line in Lines)
        {
            line.Measure(availableSize);
            Rect bounds = line.Bounds;
            width = MathF.Max(bounds.Width, width);
            height += bounds.Height;
        }

        return new Size(width, height);
    }
}
