using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.TextFormatting;

using static Beutl.Media.TextFormatting.FormattedTextParser;
using static Beutl.Media.TextFormatting.FormattedTextTokenizer;

namespace Beutl.Graphics.Shapes;

public class TextElementsBuilder
{
    private readonly List<TextElement> _elements = new();
    private readonly Stack<FontFamily> _fontFamily = new();
    private readonly Stack<FontWeight> _fontWeight = new();
    private readonly Stack<FontStyle> _fontStyle = new();
    private readonly Stack<float> _size = new();
    private readonly Stack<IBrush?> _brush = new();
    private readonly Stack<IPen?> _pen = new();
    private readonly Stack<float> _spacing = new();
    private readonly Stack<Thickness> _margin = new();
    private readonly FormattedTextInfo _initialOptions;
    private FontFamily _curFontFamily;
    private FontWeight _curFontWeight;
    private FontStyle _curFontStyle;
    private float _curSize;
    private IBrush? _curBrush;
    private IPen? _curPen;
    private float _curSpacing;
    private bool _singleLine;

    public TextElementsBuilder(FormattedTextInfo initialOptions)
    {
        _initialOptions = initialOptions;
        _curFontFamily = initialOptions.Typeface.FontFamily;
        _curFontWeight = initialOptions.Typeface.Weight;
        _curFontStyle = initialOptions.Typeface.Style;
        _curSize = initialOptions.Size;
        _curBrush = initialOptions.Brush;
        _curPen = initialOptions.Pen;
        _curSpacing = initialOptions.Space;
    }

    public ReadOnlySpan<TextElement> Items => CollectionsMarshal.AsSpan(_elements);

    public void PushFontFamily(FontFamily? font)
    {
        if (font != null)
        {
            _fontFamily.Push(_curFontFamily);
            _curFontFamily = font;
        }
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

    public void PushBrush(IBrush brush)
    {
        _brush.Push(_curBrush);
        _curBrush = brush;
    }

    public void PushPen(IPen pen)
    {
        _pen.Push(_curPen);
        _curPen = pen;
    }

    public void PushSpacing(float value)
    {
        _spacing.Push(_curSpacing);
        _curSpacing = value;
    }

    public void PushSingleLine()
    {
        _singleLine = true;
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
            case Options.Brush:
                _curBrush = _brush.PopOrDefault(_initialOptions.Brush);
                break;
            case Options.Pen:
                _curPen = _pen.PopOrDefault(_initialOptions.Pen);
                break;
            case Options.Spacing:
                _curSpacing = _spacing.PopOrDefault(_initialOptions.Space);
                break;
            case Options.SingleLine:
                _singleLine = false;
                break;
            default:
                break;
        }
    }

    public void Append(string text)
    {
        _elements.Add(new TextElement()
        {
            Text = text,
            FontFamily = _curFontFamily,
            FontStyle = _curFontStyle,
            FontWeight = _curFontWeight,
            Size = _curSize,
            Brush = _curBrush,
            Spacing = _curSpacing,
            IgnoreLineBreaks = _singleLine,
            Pen = _curPen,
        });
    }

    public void AppendTokens(Span<Token> tokens)
    {
        bool noParse = false;

        foreach (Token token in tokens)
        {
            if (!noParse && token.Type == TokenType.TagStart &&
                TryParseTag(token.Text, out TagInfo tag))
            {
                // 開始タグ
                if (tag.TryGetFont(out FontFamily? font1))
                    PushFontFamily(font1);
                else if (tag.TryGetSize(out float size1))
                    PushSize(size1);
                else if (tag.TryGetColor(out Color color1))
                    PushBrush(color1.ToImmutableBrush());
                else if (tag.TryGetStroke(out IPen? pen1))
                    PushPen(pen1);
                else if (tag.TryGetCharSpace(out float space1))
                    PushSpacing(space1);
                else if (tag.TryGetFontStyle(out FontStyle fontStyle1))
                    PushFontStyle(fontStyle1);
                else if (tag.TryGetFontWeight(out FontWeight fontWeight1))
                    PushFontWeight(fontWeight1);
                else if (tag.Type == TagType.NoParse)
                    noParse = true;
                else if (tag.Type == TagType.SingleLine)
                    PushSingleLine();

                continue;
            }
            else if (token.Type == TokenType.TagClose)
            {
                TagType closeTagType = GetCloseTagType(token.Text);
                if (closeTagType == TagType.NoParse)
                {
                    noParse = false;
                }
                else if (!noParse)
                {
                    Options options = ToOptions(closeTagType, token.Text.AsSpan());
                    Pop(options);
                }
            }

            if (token.Type == TokenType.Content || noParse)
            {
                Append(token.Text.AsSpan().ToString());
            }
        }
    }

    private static Options ToOptions(TagType tagType, ReadOnlySpan<char> text)
    {
        return tagType switch
        {
            TagType.Font => Options.FontFamily,
            TagType.Size => Options.Size,
            TagType.Color or TagType.ColorHash => Options.Brush,
            TagType.Stroke => Options.Pen,
            TagType.CharSpace => Options.Spacing,
            TagType.FontWeightBold or TagType.FontWeight => Options.FontWeight,
            TagType.FontStyle or TagType.FontStyleItalic => Options.FontStyle,
            TagType.SingleLine => Options.SingleLine,
            _ => Options.Unknown
        };
    }

    public enum Options
    {
        FontFamily,
        FontWeight,
        FontStyle,
        Size,
        Brush,
        Pen,
        Spacing,
        SingleLine,
        Unknown
    }
}
