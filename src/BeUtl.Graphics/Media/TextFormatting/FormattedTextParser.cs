using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using BeUtl.Graphics;

namespace BeUtl.Media.TextFormatting;

public readonly record struct StringSpan(string Source, int Start, int Length)
{
    public static StringSpan Empty => new(string.Empty, 0, 0);

    public override string ToString()
    {
        return AsSpan().ToString();
    }

    public ReadOnlySpan<char> AsSpan()
    {
        return IsValid() ? Source.AsSpan(Start, Length) : default;
    }

    public StringSpan Slice(int start, int length)
    {
        var r = new StringSpan(Source, Start + start, length);
        return r;
    }

    public StringSpan Slice(int start)
    {
        var r = new StringSpan(Source, Start + start, Length - start);
        return r;
    }

    public bool IsValid()
    {
        return Start >= 0 && (Start + Length) <= Source.Length;
    }
}

public readonly struct FormattedTextParser
{
    private readonly string _s;

    public FormattedTextParser(string s)
    {
        _s = s;
    }

    public List<TextLine> ToLines(FormattedTextInfo defaultProps)
    {
        List<Token> tokens = Tokenize();
        var lines = new List<TextLine>();

        // 行を追加
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Type == TokenType.NewLine)
            {
                lines.Add(new TextLine());
            }
        }

        int lineNum = 0;
        var font = new Stack<FontFamily>();
        var fontWeight = new Stack<FontWeight>();
        var fontStyle = new Stack<FontStyle>();
        var size = new Stack<float>();
        var color = new Stack<Color>();
        var space = new Stack<float>();
        var margin = new Stack<Thickness>();

        font.Push(defaultProps.Typeface.FontFamily);
        fontWeight.Push(defaultProps.Typeface.Weight);
        fontStyle.Push(defaultProps.Typeface.Style);
        size.Push(defaultProps.Size);
        color.Push(defaultProps.Color);
        space.Push(defaultProps.Space);
        margin.Push(defaultProps.Margin);
        bool noParse = false;

        for (int i1 = 0; i1 < tokens.Count; i1++)
        {
            Token token = tokens[i1];

            if (!noParse && token.Type == TokenType.TagStart &&
                TryParseTag(token.Text, out TagInfo tag))
            {
                // 開始タグ
                if (tag.TryGetFont(out FontFamily font1))
                {
                    font.Push(font1);
                }
                else if (tag.TryGetSize(out float size1))
                {
                    size.Push(size1);
                }
                else if (tag.TryGetColor(out Color color1))
                {
                    color.Push(color1);
                }
                else if (tag.TryGetCharSpace(out float space1))
                {
                    space.Push(space1);
                }
                else if (tag.TryGetMargin(out Thickness margin1))
                {
                    margin.Push(margin1);
                }
                else if (tag.TryGetFontStyle(out FontStyle fontStyle1))
                {
                    fontStyle.Push(fontStyle1);
                }
                else if (tag.TryGetFontWeight(out FontWeight fontWeight1))
                {
                    fontWeight.Push(fontWeight1);
                }
                else if (tag.Type == TagType.NoParse)
                {
                    noParse = true;
                }
                else
                {
                    throw new Exception($"{tag.Value} is invalid tag.");
                }

                continue;
            }
            else if (token.Type == TokenType.TagClose)
            {
                TagType closeTagType = GetCloseTagType(token.Text);
                if (closeTagType == TagType.NoParse)
                {
                    noParse = false;
                }

                if (!noParse)
                {
                    switch (closeTagType)
                    {
                        case TagType.Invalid:
                            goto default;
                        case TagType.Font:
                            font.Pop();
                            break;
                        case TagType.Size:
                            size.Pop();
                            break;
                        case TagType.Color:
                            color.Pop();
                            break;
                        case TagType.ColorHash:
                            color.Pop();
                            break;
                        case TagType.CharSpace:
                            space.Pop();
                            break;
                        case TagType.Margin:
                            margin.Pop();
                            break;
                        case TagType.FontWeightBold:
                            fontWeight.Pop();
                            break;
                        case TagType.FontWeight:
                            fontWeight.Pop();
                            break;
                        case TagType.FontStyle:
                            fontStyle.Pop();
                            break;
                        case TagType.FontStyleItalic:
                            fontStyle.Pop();
                            break;
                        case TagType.NoParse:
                            noParse = false;
                            break;
                        default:
                            throw new Exception($"{token.Text} is invalid tag.");
                    }
                }
            }

            if (token.Type == TokenType.Content)
            {
                FontFamily font1 = font.PeekOrDefault(defaultProps.Typeface.FontFamily);
                FontStyle style1 = fontStyle.PeekOrDefault(defaultProps.Typeface.Style);
                FontWeight weight1 = fontWeight.PeekOrDefault(defaultProps.Typeface.Weight);

                lines[lineNum].Elements.Add(new TextElement()
                {
                    Text = token.Text.AsSpan().ToString(),
                    Typeface = new Typeface(font1, style1, weight1),
                    Size = size.PeekOrDefault(defaultProps.Size),
                    Foreground = color.PeekOrDefault(defaultProps.Color).ToBrush(),
                    Spacing = space.PeekOrDefault(defaultProps.Space),
                    Margin = margin.PeekOrDefault(defaultProps.Margin),
                });
            }
            else if (token.Type == TokenType.NewLine)
            {
                lineNum++;
            }
            else if (noParse)
            {
                FontFamily font1 = font.PeekOrDefault(defaultProps.Typeface.FontFamily);
                FontStyle style1 = fontStyle.PeekOrDefault(defaultProps.Typeface.Style);
                FontWeight weight1 = fontWeight.PeekOrDefault(defaultProps.Typeface.Weight);

                lines[lineNum].Elements.Add(new TextElement()
                {
                    Text = token.ToString(),
                    Typeface = new Typeface(font1, style1, weight1),
                    Size = size.PeekOrDefault(defaultProps.Size),
                    Foreground = color.PeekOrDefault(defaultProps.Color).ToBrush(),
                    Spacing = space.PeekOrDefault(defaultProps.Space),
                    Margin = margin.PeekOrDefault(defaultProps.Margin),
                });
            }
        }

        lines.RemoveAll(i =>
        {
            if (i.Elements.Count < 1)
            {
                i.Dispose();
                return true;
            }
            else
            {
                return false;
            }
        });

        return lines;
    }

    // '_s'を全てトークン化
    public List<Token> Tokenize()
    {
        var result = new List<Token>();
        ReadOnlySpan<char> span = _s.AsSpan();

        foreach (ReadOnlySpan<char> linesp in span.EnumerateLines())
        {
            int start = span.IndexOf(linesp);
            int len = linesp.Length;

            Tokenize(new StringSpan(_s, start, len), result);

            result.Add(new Token(StringSpan.Empty, TokenType.NewLine));
        }

        return result;
    }

    private void Process(StringSpan s, int start, int length, List<Token> result)
    {
        StringSpan prev = s.Slice(0, start);
        StringSpan tag = s.Slice(start, length);
        StringSpan next = s.Slice(start + length);

        if (prev.Length > 0 && prev.IsValid())
        {
            Tokenize(prev, result);
        }

        if (tag.Length > 0 && tag.IsValid())
        {
            TokenizeTag(tag, result);
        }

        if (next.Length > 0 && next.IsValid())
        {
            Tokenize(next, result);
        }
    }

    // 文字列をトークン化
    // Tokenize -> Process -> Tokenize
    //                     -> TokenizeTag
    //                     -> Tokenize
    private void Tokenize(StringSpan s, List<Token> result)
    {
        ReadOnlySpan<char> span = s.AsSpan();
        int tagStart = span.IndexOf('<');
        int tagEnd = span.IndexOf('>');

        bool isMatch = tagStart >= 0 && tagEnd >= 0 &&
            tagStart < tagEnd;

        if (isMatch)
        {
            Process(s, tagStart, tagEnd - tagStart + 1, result);
        }
        else if (s.Length > 0)
        {
            result.Add(new Token(s, TokenType.Content));
        }
    }

    // タグを解析
    private static bool TryParseTag(StringSpan tag, out TagInfo result)
    {
        ReadOnlySpan<char> span = tag.AsSpan();
        if (span.IsWhiteSpace())
        {
            result = default;
            return false;
        }

        int assignIdx = span.IndexOf('=');
        StringSpan first = tag;
        StringSpan second = StringSpan.Empty;
        if (assignIdx >= 0)
        {
            first = tag.Slice(0, assignIdx);
            second = tag.Slice(assignIdx + 1);
        }
        TagType tagType = GetTagType(first);

        // "<#fffff>" みたいなタグ
        if (tagType is TagType.ColorHash or TagType.FontWeightBold or TagType.FontStyleItalic or TagType.NoParse)
        {
            result = new TagInfo(first, tagType);
            return true;
        }
        else if (second.Length > 0)
        {
            result = new TagInfo(second, tagType);
            return true;
        }
        else
        {
            result = new TagInfo(tag, TagType.Invalid);
            return false;
        }
    }

    // タグをトークンにして追加
    private static void TokenizeTag(StringSpan s, List<Token> result)
    {
        // TagClose
        if (s.AsSpan().StartsWith(stackalloc char[] { '<', '/' }, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new Token(s, TokenType.TagClose));
        }
        else
        {
            result.Add(new Token(s, TokenType.TagStart));
        }
    }

    // 終了タグの種類を取得
    private static TagType GetCloseTagType(StringSpan tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan();

        if (span.StartsWith("<"))
        {
            span = span[1..];
        }
        if (span.EndsWith(">"))
        {
            span = span[..^1];
        }

        if (span.SequenceEqual("/font"))
        {
            return TagType.Font;
        }
        else if (span.SequenceEqual("/size"))
        {
            return TagType.Size;
        }
        else if (span.SequenceEqual("/color"))
        {
            return TagType.Color;
        }
        else if (span.SequenceEqual("/cspace"))
        {
            return TagType.CharSpace;
        }
        else if (span.SequenceEqual("/margin"))
        {
            return TagType.Margin;
        }
        else if (span.SequenceEqual("/weight"))
        {
            return TagType.FontWeight;
        }
        else if (span.SequenceEqual("/style"))
        {
            return TagType.FontStyle;
        }
        else if (span.SequenceEqual("/bold") || span.SequenceEqual("/b"))
        {
            return TagType.FontWeightBold;
        }
        else if (span.SequenceEqual("/italic") || span.SequenceEqual("/i"))
        {
            return TagType.FontStyleItalic;
        }
        else if (span.SequenceEqual("/noparse"))
        {
            return TagType.NoParse;
        }
        else if (span.StartsWith("/#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    // 開始タグの種類を取得
    private static TagType GetTagType(StringSpan tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan();

        if (span.StartsWith("<"))
        {
            span = span[1..];
        }
        if (span.EndsWith(">"))
        {
            span = span[..^1];
        }

        var d = span.ToString();
        if (span.SequenceEqual("font"))
        {
            return TagType.Font;
        }
        else if (span.SequenceEqual("size"))
        {
            return TagType.Size;
        }
        else if (span.SequenceEqual("color"))
        {
            return TagType.Color;
        }
        else if (span.SequenceEqual("cspace"))
        {
            return TagType.CharSpace;
        }
        else if (span.SequenceEqual("margin"))
        {
            return TagType.Margin;
        }
        else if (span.SequenceEqual("weight"))
        {
            return TagType.FontWeight;
        }
        else if (span.SequenceEqual("style"))
        {
            return TagType.FontStyle;
        }
        else if (span.SequenceEqual("bold") || span.SequenceEqual("b"))
        {
            return TagType.FontWeightBold;
        }
        else if (span.SequenceEqual("italic") || span.SequenceEqual("i"))
        {
            return TagType.FontStyleItalic;
        }
        else if (span.SequenceEqual("noparse"))
        {
            return TagType.NoParse;
        }
        else if (span.StartsWith("#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    public record struct Token(StringSpan Text, TokenType Type)
    {
        public override string ToString()
        {
            if (Type == TokenType.NewLine)
            {
                return "newline";
            }
            return Text.AsSpan().ToString();
        }
    }

    public enum TokenType
    {
        TagStart,
        TagClose,
        Content,
        NewLine,
    }

    public record struct TagInfo(StringSpan Value, TagType Type)
    {
        public static readonly TagInfo Invalid = new(StringSpan.Empty, TagType.Invalid);

        public bool TryGetColor(out Color color)
        {
            if (Type is TagType.Color or TagType.ColorHash &&
                Color.TryParse(RemoveQuotation(Value), out color))
            {
                return true;
            }

            color = default;
            return false;
        }

        public bool TryGetFont(out FontFamily font)
        {
            if (Type == TagType.Font)
            {
                ReadOnlySpan<char> str = RemoveQuotation(Value);
                font = new FontFamily(str.ToString());
                if (FontManager.Instance.FontFamilies.Contains(font))
                {
                    return true;
                }
            }

            font = default;
            return false;
        }

        public bool TryGetSize(out float size)
        {
            if (Type is TagType.Size &&
                float.TryParse(RemoveQuotation(Value), NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            {
                return true;
            }

            size = 0;
            return false;
        }

        public bool TryGetCharSpace(out float space)
        {
            if (Type is TagType.CharSpace &&
                float.TryParse(RemoveQuotation(Value), NumberStyles.Float, CultureInfo.InvariantCulture, out space))
            {
                return true;
            }

            space = 0;
            return false;
        }

        public bool TryGetMargin(out Thickness margin)
        {
            if (Type is TagType.Margin &&
                Thickness.TryParse(RemoveQuotation(Value), out margin))
            {
                return true;
            }

            margin = default;
            return false;
        }

        public bool TryGetFontWeight(out FontWeight weight)
        {
            if (Type is TagType.FontWeight or TagType.FontWeightBold)
            {
                ReadOnlySpan<char> str = RemoveQuotation(Value);
                if (int.TryParse(str, out int weightNum))
                {
                    weight = (FontWeight)weightNum;
                    return true;
                }
                else
                {
                    weight = str switch
                    {
                        var thin when thin.SequenceEqual("thin") => FontWeight.Thin,
                        var ulight when ulight.SequenceEqual("ultra-light") => FontWeight.UltraLight,
                        var light when light.SequenceEqual("light") => FontWeight.Light,
                        var slight when slight.SequenceEqual("semi-light") => FontWeight.SemiLight,
                        var reg when reg.SequenceEqual("regular") => FontWeight.Regular,
                        var med when med.SequenceEqual("medium") => FontWeight.Medium,
                        var sbold when sbold.SequenceEqual("semi-bold") => FontWeight.SemiBold,
                        var bold when bold.SequenceEqual("bold") => FontWeight.Bold,
                        var b when b.SequenceEqual("b") => FontWeight.Bold,
                        var ubold when ubold.SequenceEqual("ultra-bold") => FontWeight.UltraBold,
                        var black when black.SequenceEqual("black") => FontWeight.Black,
                        var ublack when ublack.SequenceEqual("ultra-black") => FontWeight.UltraBlack,
                        _ => (FontWeight)(-1),
                    };

                    if ((int)weight != -1)
                    {
                        return true;
                    }
                }
            }

            weight = FontWeight.Regular;
            return false;
        }

        public bool TryGetFontStyle(out FontStyle style)
        {
            if (Type is TagType.FontStyle or TagType.FontStyleItalic)
            {
                ReadOnlySpan<char> str = RemoveQuotation(Value);
                style = str switch
                {
                    var normal when normal.SequenceEqual("normal") => FontStyle.Normal,
                    var italic when italic.SequenceEqual("italic") => FontStyle.Italic,
                    var i when i.SequenceEqual("i") => FontStyle.Italic,
                    var oblique when oblique.SequenceEqual("oblique") => FontStyle.Oblique,
                    _ => (FontStyle)(-1),
                };

                if ((int)style != -1)
                {
                    return true;
                }
            }

            style = FontStyle.Normal;
            return false;
        }

        private static ReadOnlySpan<char> RemoveQuotation(StringSpan s)
        {
            var span = s.AsSpan();
            if (span.StartsWith(stackalloc char[] { '<' }, StringComparison.OrdinalIgnoreCase))
            {
                span = span[1..];
            }
            if (span.EndsWith(stackalloc char[] { '>' }, StringComparison.OrdinalIgnoreCase))
            {
                span = span[..^1];
            }
            if (span.StartsWith(stackalloc char[] { '\"' }, StringComparison.OrdinalIgnoreCase) ||
                span.StartsWith(stackalloc char[] { '\'' }, StringComparison.OrdinalIgnoreCase))
            {
                span = span[1..];
            }
            if (span.EndsWith(stackalloc char[] { '\"' }, StringComparison.OrdinalIgnoreCase) ||
               span.EndsWith(stackalloc char[] { '\'' }, StringComparison.OrdinalIgnoreCase))
            {
                span = span[..^1];
            }

            return span;
        }
    }

    public enum TagType
    {
        Invalid,
        Font,
        Size,
        Color,
        ColorHash,
        CharSpace,
        FontWeightBold,
        FontWeight,
        FontStyle,
        FontStyleItalic,
        Margin,
        NoParse
    }
}
