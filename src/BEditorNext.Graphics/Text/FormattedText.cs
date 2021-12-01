using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using OpenCvSharp;
using SkiaSharp;

namespace BEditorNext.Graphics;

internal static class SharedSkiaObject
{
    public static SKPaint Paint { get; } = new();
}

public class FontFamily
{

}

public class TextElement : IDisposable
{
    private readonly SKPaint _paint = new();
    private SKTypeface _font = SKTypeface.Default;
    private float _size;

    ~TextElement()
    {
        Dispose();
    }

    public FontWeight Weight { get; set; } = FontWeight.Normal;

    public FontStyle Width { get; set; } = FontStyle.Normal;

    public SKTypeface Font
    {
        get => _font;
        set
        {
            if (_font != value)
            {
                _font = value;
                OnFontOrSizeChanged();
            }
        }
    }

    public float Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                OnFontOrSizeChanged();
            }
        }
    }

    public Color Color { get; set; }

    public float Spacing { get; set; }

    public string Text { get; set; } = string.Empty;

    public Thickness Margin { get; set; }

    public SKFontMetrics FontMetrics { get; private set; }

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _paint.Dispose();
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // Marginを考慮しない
    public Size Measure()
    {
        float w = _paint.MeasureText(Text);

        return new Size(
            w + (Text.Length - 1) * Spacing,
            FontMetrics.Descent - FontMetrics.Ascent);
    }

    private void OnFontOrSizeChanged()
    {
        _paint.TextSize = Size;
        _paint.Typeface = Font;
        FontMetrics = _paint.FontMetrics;
    }
}

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
            height = MathF.Max(bounds.Height, height);
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
        graphics.SaveMatrix();

        float prevBottom = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            TextLine line = Lines[i];
            Size lineBounds = line.Measure();
            float ascent = line.MinAscent();

            graphics.SaveMatrix();
            graphics.Translate(new(0, prevBottom - ascent));

            float prevRight = 0;
            foreach (TextElement element in line.Elements)
            {
                graphics.Translate(new(prevRight, 0));
                Size elementBounds = element.Measure();

                graphics.DrawText(element);

                prevRight = elementBounds.Width;
            }

            prevBottom += lineBounds.Height;
            graphics.RestoreMatrix();
        }

        graphics.RestoreMatrix();
    }
}

public record struct FormattedTextInfo(SKTypeface Font, float Size, Color Color, float Space, Thickness Margin);
