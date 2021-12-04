using System.Globalization;
using System.Text.RegularExpressions;

using BEditorNext.Graphics;

namespace BEditorNext.Media.TextFormatting;

public readonly struct FormattedTextTokenizer
{
    private static readonly Regex s_tagRegex = new(@"^(?<prev>.*)\<(?<tag>.*)\>(?<next>.*)$");
    private readonly string _s;

    public FormattedTextTokenizer(string s)
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
                    Text = Regex.Unescape(token.Text),
                    Typeface = new Typeface(font1, style1, weight1),
                    Size = size.PeekOrDefault(defaultProps.Size),
                    Color = color.PeekOrDefault(defaultProps.Color),
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
                    Color = color.PeekOrDefault(defaultProps.Color),
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
        string[] lines = _s.ReplaceLineEndings("\n").Split('\n');
        var result = new List<Token>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Tokenize(Regex.Escape(line), result);

            result.Add(new Token(string.Empty, TokenType.NewLine));
        }

        return result;
    }

    private void Process(Match match, List<Token> result)
    {
        string prev = match.Groups["prev"].Value;
        string tag = match.Groups["tag"].Value;
        string next = match.Groups["next"].Value;

        if (!string.IsNullOrEmpty(prev))
        {
            Tokenize(prev, result);
        }

        if (!string.IsNullOrEmpty(tag))
        {
            TokenizeTag(tag, result);
        }

        if (!string.IsNullOrEmpty(next))
        {
            Tokenize(next, result);
        }
    }

    // 文字列をトークン化
    // Tokenize -> Process -> Tokenize
    //                     -> TokenizeTag
    //                     -> Tokenize
    private void Tokenize(string s, List<Token> result)
    {
        Match tagMatch = s_tagRegex.Match(s);

        if (tagMatch.Success)
        {
            Process(tagMatch, result);
        }
        else if (!string.IsNullOrEmpty(s))
        {
            result.Add(new Token(s, TokenType.Content));
        }
    }

    // タグを解析
    private static bool TryParseTag(string tag, out TagInfo result)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            result = default;
            return false;
        }

        string[] array = tag.Split('=');
        TagType tagType = GetTagType(array[0]);

        // "<#fffff>" みたいなタグ
        if (tagType is TagType.ColorHash or TagType.FontWeightBold or TagType.FontStyleItalic or TagType.NoParse)
        {
            result = new TagInfo(array[0], tagType);
            return true;
        }
        else if (array.Length == 2)
        {
            result = new TagInfo(array[1], tagType);
            return true;
        }
        else
        {
            result = new TagInfo(tag, TagType.Invalid);
            return false;
        }
    }

    // タグをトークンにして追加
    private static void TokenizeTag(string s, List<Token> result)
    {
        // TagClose
        if (s.StartsWith('/'))
        {
            result.Add(new Token(s, TokenType.TagClose));
        }
        else
        {
            result.Add(new Token(s, TokenType.TagStart));
        }
    }

    // 終了タグの種類を取得
    private static TagType GetCloseTagType(string tag)
    {
        switch (tag)
        {
            case "/font":
                return TagType.Font;
            case "/size":
                return TagType.Size;
            case "/color":
                return TagType.Color;
            case "/cspace":
                return TagType.CharSpace;
            case "/margin":
                return TagType.Margin;
            case "/weight":
                return TagType.FontWeight;
            case "/style":
                return TagType.FontStyle;
            case "/b":
            case "/bold":
                return TagType.FontWeightBold;
            case "/i":
            case "/italic":
                return TagType.FontStyleItalic;
            case "/#":
                return TagType.ColorHash;
            case "/noparse":
                return TagType.NoParse;
            default:
                break;
        }

        if (tag.StartsWith("/\\#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    // 開始タグの種類を取得
    private static TagType GetTagType(string tag)
    {
        switch (tag)
        {
            case "font":
                return TagType.Font;
            case "size":
                return TagType.Size;
            case "color":
                return TagType.Color;
            case "cspace":
                return TagType.CharSpace;
            case "margin":
                return TagType.Margin;
            case "weight":
                return TagType.FontWeight;
            case "style":
                return TagType.FontStyle;
            case "b":
            case "bold":
                return TagType.FontWeightBold;
            case "i":
            case "italic":
                return TagType.FontStyleItalic;
            case "noparse":
                return TagType.NoParse;
            default:
                break;
        }

        if (tag.StartsWith("\\#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    public record struct Token(string Text, TokenType Type)
    {
        public override string ToString()
        {
            if (Type is TokenType.TagStart or TokenType.TagClose)
            {
                return $"<{Regex.Unescape(Text)}>";
            }
            else
            {
                return base.ToString() ?? string.Empty;
            }
        }
    }

    public enum TokenType
    {
        TagStart,
        TagClose,
        Content,
        NewLine,
    }

    public record struct TagInfo(string Value, TagType Type)
    {
        public static readonly TagInfo Invalid = new(string.Empty, TagType.Invalid);

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
                string str = RemoveQuotation(Value);
                font = new FontFamily(str);
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
                string str = RemoveQuotation(Value);
                if (int.TryParse(str, out int weightNum))
                {
                    weight = (FontWeight)weightNum;
                    return true;
                }
                else
                {
                    weight = str switch
                    {
                        "thin" => FontWeight.Thin,
                        "ultra-light" => FontWeight.UltraLight,
                        "light" => FontWeight.Light,
                        "semi-light" => FontWeight.SemiLight,
                        "regular" => FontWeight.Regular,
                        "medium" => FontWeight.Medium,
                        "semi-bold" => FontWeight.SemiBold,
                        "bold" => FontWeight.Bold,
                        "b" => FontWeight.Bold,
                        "ultra-bold" => FontWeight.UltraBold,
                        "black" => FontWeight.Black,
                        "ultra-black" => FontWeight.UltraBlack,
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
                string str = RemoveQuotation(Value);
                style = str switch
                {
                    "normal" => FontStyle.Normal,
                    "italic" => FontStyle.Italic,
                    "i" => FontStyle.Italic,
                    "oblique" => FontStyle.Oblique,
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

        private static string RemoveQuotation(string s)
        {
            return s.Replace("\"", "")
                .Replace("'", "")
                .Replace("\\", "");
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
