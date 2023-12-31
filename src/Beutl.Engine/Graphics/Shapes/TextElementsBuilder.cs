using System.Runtime.InteropServices;

using Beutl.Media;
using Beutl.Media.TextFormatting;

using static Beutl.Media.TextFormatting.FormattedTextParser;
using static Beutl.Media.TextFormatting.FormattedTextTokenizer;

namespace Beutl.Graphics.Shapes;

public class TextElementsBuilder(FormattedTextInfo initialOptions)
{
    private readonly List<TextElement> _elements = [];
    private readonly Stack<FontFamily> _fontFamily = [];
    private readonly Stack<FontWeight> _fontWeight = [];
    private readonly Stack<FontStyle> _fontStyle = [];
    private readonly Stack<float> _size = [];
    private readonly Stack<IBrush?> _brush = [];
    private readonly Stack<IPen?> _pen = [];
    private readonly Stack<float> _spacing = [];
    private readonly Stack<Thickness> _margin = [];
    private FontFamily _curFontFamily = initialOptions.Typeface.FontFamily;
    private FontWeight _curFontWeight = initialOptions.Typeface.Weight;
    private FontStyle _curFontStyle = initialOptions.Typeface.Style;
    private float _curSize = initialOptions.Size;
    private IBrush? _curBrush = initialOptions.Brush;
    private IPen? _curPen = initialOptions.Pen;
    private float _curSpacing = initialOptions.Space;
    private bool _singleLine;

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
                _curFontFamily = _fontFamily.PopOrDefault(initialOptions.Typeface.FontFamily);
                break;
            case Options.FontWeight:
                _curFontWeight = _fontWeight.PopOrDefault(initialOptions.Typeface.Weight);
                break;
            case Options.FontStyle:
                _curFontStyle = _fontStyle.PopOrDefault(initialOptions.Typeface.Style);
                break;
            case Options.Size:
                _curSize = _size.PopOrDefault(initialOptions.Size);
                break;
            case Options.Brush:
                _curBrush = _brush.PopOrDefault(initialOptions.Brush);
                break;
            case Options.Pen:
                _curPen = _pen.PopOrDefault(initialOptions.Pen);
                break;
            case Options.Spacing:
                _curSpacing = _spacing.PopOrDefault(initialOptions.Space);
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
                    Options options = ToOptions(closeTagType);
                    Pop(options);
                }
            }

            if (token.Type == TokenType.Content || noParse)
            {
                Append(token.Text.AsSpan().ToString());
            }
        }
    }

    private static Options ToOptions(TagType tagType)
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
