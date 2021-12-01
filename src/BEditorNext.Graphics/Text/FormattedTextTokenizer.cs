using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

using SkiaSharp;

namespace BEditorNext.Graphics;

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
        var font = new Stack<SKTypeface>();
        var size = new Stack<float>();
        var color = new Stack<Color>();
        var space = new Stack<float>();
        var margin = new Stack<Thickness>();

        font.Push(defaultProps.Font);
        size.Push(defaultProps.Size);
        color.Push(defaultProps.Color);
        space.Push(defaultProps.Space);
        margin.Push(defaultProps.Margin);

        for (int i1 = 0; i1 < tokens.Count; i1++)
        {
            Token token = tokens[i1];
            if (token.Type == TokenType.TagStart)
            {
                List<TagInfo> tags = ParseTags(token.Text);
                for (int i = 0; i < tags.Count; i++)
                {
                    TagInfo tag = tags[i];
                    if (tag.TryGetFont(out string? fontName))
                    {
                        // Todo: フォントを指定
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
                    else
                    {
                        throw new Exception($"{tag.Value} is invalid tag.");
                    }
                }
            }
            else if (token.Type == TokenType.TagClose)
            {
                switch (GetCloseTagType(token.Text))
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
                    default:
                        throw new Exception($"{token.Text} is invalid tag.");
                }
            }
            else if (token.Type == TokenType.Content)
            {
                lines[lineNum].Elements.Add(new TextElement()
                {
                    Text = token.Text,
                    Font = font.PeekOrDefault(defaultProps.Font),
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

    private static List<TagInfo> ParseTags(string tags)
    {
        var result = new List<TagInfo>();
        string[] array1 = tags.Split(' ');
        for (int i = 0; i < array1.Length; i++)
        {
            string item = array1[i];
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            string[] array = item.Split('=');
            string tag = array[0];
            TagType tagType = GetTagType(tag);

            // "<#fffff>" みたいなタグ
            if (tagType is TagType.ColorHash/* or ...*/)
            {
                result.Add(new TagInfo(tag, tagType));
            }
            else if (array.Length == 2)
            {
                result.Add(new TagInfo(array[1], tagType));
            }
            else
            {
                result.Add(new TagInfo(tag, TagType.Invalid));
            }
        }

        return result;
    }

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
            case "/#":
                return TagType.ColorHash;
            default:
                break;
        }

        if (tag.StartsWith("/\\#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

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
            default:
                break;
        }

        if (tag.StartsWith("\\#"))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    public record struct Token(string Text, TokenType Type);

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

        public bool TryGetFont([NotNullWhen(true)] out string? font)
        {
            if (Type == TagType.Font)
            {
                // Todo: FontManagerにフォントが存在するかを確認
                font = RemoveQuotation(Value);
                return true;
            }

            font = null;
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
            if (Type is TagType.CharSpace &&
                Thickness.TryParse(RemoveQuotation(Value), out margin))
            {
                return true;
            }

            margin = default;
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
        //Bold,
        //Italic,
        Margin,
    }
}
