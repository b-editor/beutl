using BeUtl.Graphics;

namespace BeUtl.Media.TextFormatting;

public class TextLine : IDisposable
{
    public IList<TextElement> Elements { get; } = new List<TextElement>();

    public bool IsDisposed { get; private set; }

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
