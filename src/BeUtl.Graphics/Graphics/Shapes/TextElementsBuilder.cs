using System.Runtime.InteropServices;

using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.TextFormatting;

namespace BeUtl.Graphics.Shapes;

public class TextElementsBuilder
{
    private readonly List<TextElement_> _elements = new();
    private readonly Stack<FontFamily> _fontFamily = new();
    private readonly Stack<FontWeight> _fontWeight = new();
    private readonly Stack<FontStyle> _fontStyle = new();
    private readonly Stack<float> _size = new();
    private readonly Stack<Color> _color = new();
    private readonly Stack<float> _spacing = new();
    private readonly Stack<Thickness> _margin = new();
    private readonly FormattedTextInfo _initialOptions;
    private FontFamily _curFontFamily;
    private FontWeight _curFontWeight;
    private FontStyle _curFontStyle;
    private float _curSize;
    private Color _curColor;
    private float _curSpacing;
    private Thickness _curMargin;

    public TextElementsBuilder(FormattedTextInfo initialOptions)
    {
        _initialOptions = initialOptions;
        _curFontFamily = initialOptions.Typeface.FontFamily;
        _curFontWeight = initialOptions.Typeface.Weight;
        _curFontStyle = initialOptions.Typeface.Style;
        _curSize = initialOptions.Size;
        _curColor = initialOptions.Color;
        _curSpacing = initialOptions.Space;
        _curMargin = initialOptions.Margin;
    }

    public ReadOnlySpan<TextElement_> Items => CollectionsMarshal.AsSpan(_elements);

    public void PushFontFamily(FontFamily font)
    {
        _fontFamily.Push(_curFontFamily);
        _curFontFamily = font;
    }

    public void PushFontWeight(FontWeight weight)
    {
        _fontWeight.Push(_curFontWeight);
        _curFontWeight = weight;
    }

    public void PushFontStyle(FontStyle style)
    {
        _fontStyle.Push(_curFontStyle);
        _curFontStyle = style;
    }

    public void PushSize(float size)
    {
        _size.Push(_curSize);
        _curSize = size;
    }

    public void PushColor(Color color)
    {
        _color.Push(_curColor);
        _curColor = color;
    }

    public void PushSpacing(float value)
    {
        _spacing.Push(_curSpacing);
        _curSpacing = value;
    }

    public void PushMargin(Thickness margin)
    {
        _margin.Push(_curMargin);
        _curMargin = margin;
    }

    public void Pop(Options options)
    {
        switch (options)
        {
            case Options.FontFamily:
                _curFontFamily = _fontFamily.PopOrDefault(_initialOptions.Typeface.FontFamily);
                break;
            case Options.FontWeight:
                _curFontWeight = _fontWeight.PopOrDefault(_initialOptions.Typeface.Weight);
                break;
            case Options.FontStyle:
                _curFontStyle = _fontStyle.PopOrDefault(_initialOptions.Typeface.Style);
                break;
            case Options.Size:
                _curSize = _size.PopOrDefault(_initialOptions.Size);
                break;
            case Options.Color:
                _curColor = _color.PopOrDefault(_initialOptions.Color);
                break;
            case Options.Spacing:
                _curSpacing = _spacing.PopOrDefault(_initialOptions.Space);
                break;
            case Options.Margin:
                _curMargin = _margin.PopOrDefault(_initialOptions.Margin);
                break;
            default:
                break;
        }
    }

    public void Append(string text)
    {
        _elements.Add(new TextElement_()
        {
            Text = text,
            FontFamily = _curFontFamily,
            FontStyle = _curFontStyle,
            FontWeight = _curFontWeight,
            Size = _curSize,
            Foreground = _curColor.ToImmutableBrush(),
            Spacing = _curSpacing,
            Margin = _curMargin,
        });
    }

    public enum Options
    {
        FontFamily,
        FontWeight,
        FontStyle,
        Size,
        Color,
        Spacing,
        Margin,
    }
}
