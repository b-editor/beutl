using BeUtl.Graphics;
using BeUtl.Styling;

namespace BeUtl.Media.TextFormatting;

public sealed class TextElements : AffectsRenders<TextElement>
{

}

public sealed class TextLine : Styleable, IAffectsRender, IDisposable, ILogicalElement
{
    public static readonly CoreProperty<TextElements> ElementsProperty;
    private readonly TextElements _elements;

    static TextLine()
    {
        ElementsProperty = ConfigureProperty<TextElements, TextLine>(nameof(Elements))
            .Accessor(o => o.Elements, (o, v) => o.Elements = v)
            .Register();
    }

    public TextLine()
    {
        _elements = new()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _elements.Invalidated += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public TextElements Elements
    {
        get => _elements;
        set => _elements.Replace(value);
    }

    public bool IsDisposed { get; private set; }

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => _elements;

    public event EventHandler? Invalidated;

    public void Dispose()
    {
        if (!IsDisposed)
        {
            foreach (TextElement item in Elements)
            {
                item.Dispose();
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public Size Measure()
    {
        float width = 0;
        float height = 0;

        foreach (TextElement element in Elements)
        {
            Size bounds = element.Measure();
            width += bounds.Width;
            width += element.Margin.Left + element.Margin.Right;

            height = MathF.Max(bounds.Height + element.Margin.Top + element.Margin.Bottom, height);
        }

        return new Size(width, height);
    }

    public float MinAscent()
    {
        float ascent = 0;
        foreach (TextElement item in Elements)
        {
            ascent = MathF.Min(item.FontMetrics.Ascent, ascent);
        }

        return ascent;
    }
}
