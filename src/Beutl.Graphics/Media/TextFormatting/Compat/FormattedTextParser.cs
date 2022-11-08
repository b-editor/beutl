using System.Runtime.InteropServices;

using Beutl.Graphics;

using static Beutl.Media.TextFormatting.FormattedTextTokenizer;
using static Beutl.Media.TextFormatting.FormattedTextParser;

namespace Beutl.Media.TextFormatting.Compat;

[Obsolete("Use TextBlock APIs.")]
public struct FormattedTextParser
{
    private readonly string _s;

    public FormattedTextParser(string s)
    {
        _s = s;
    }

    public List<TextLine> ToLines(FormattedTextInfo defaultProps)
    {
        var tokenizer = new FormattedTextTokenizer(_s)
        {
            CompatMode = true
        };
        tokenizer.Tokenize();
        List<Token> tokens = tokenizer.Result;
        Span<Token> spanTokens = CollectionsMarshal.AsSpan(tokens);
        var lines = new List<TextLine>(tokenizer.LineCount);

        // 行を追加
        foreach (Token token in spanTokens)
        {
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
        var color = new Stack<IBrush>();
        var space = new Stack<float>();
        var margin = new Stack<Thickness>();
        FontFamily curFont = defaultProps.Typeface.FontFamily;
        FontWeight curWeight = defaultProps.Typeface.Weight;
        FontStyle curStyle = defaultProps.Typeface.Style;
        float curSize = defaultProps.Size;
        IBrush curColor = defaultProps.Brush;
        float curSpace = defaultProps.Space;
        Thickness curMargin = defaultProps.Margin;
        bool noParse = false;

        foreach (Token token in spanTokens)
        {
            if (!noParse && token.Type == TokenType.TagStart &&
                TryParseTag(token.Text, out TagInfo tag))
            {
                // 開始タグ
                if (tag.TryGetFont(out FontFamily font1))
                {
                    font.Push(curFont);
                    curFont = font1;
                }
                else if (tag.TryGetSize(out float size1))
                {
                    size.Push(curSize);
                    curSize = size1;
                }
                else if (tag.TryGetColor(out Color color1))
                {
                    color.Push(curColor);
                    curColor = color1.ToImmutableBrush();
                }
                else if (tag.TryGetCharSpace(out float space1))
                {
                    space.Push(curSpace);
                    curSpace = space1;
                }
                else if (tag.TryGetMargin(out Thickness margin1))
                {
                    margin.Push(curMargin);
                    curMargin = margin1;
                }
                else if (tag.TryGetFontStyle(out FontStyle fontStyle1))
                {
                    fontStyle.Push(curStyle);
                    curStyle = fontStyle1;
                }
                else if (tag.TryGetFontWeight(out FontWeight fontWeight1))
                {
                    fontWeight.Push(curWeight);
                    curWeight = fontWeight1;
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
                            curFont = font.PopOrDefault(defaultProps.Typeface.FontFamily);
                            break;
                        case TagType.Size:
                            curSize = size.PopOrDefault(defaultProps.Size);
                            break;
                        case TagType.Color:
                        case TagType.ColorHash:
                            curColor = color.PopOrDefault(defaultProps.Brush);
                            break;
                        case TagType.CharSpace:
                            curSpace = space.PopOrDefault(defaultProps.Space);
                            break;
                        case TagType.Margin:
                            curMargin = margin.PopOrDefault(defaultProps.Margin);
                            break;
                        case TagType.FontWeightBold:
                        case TagType.FontWeight:
                            curWeight = fontWeight.PopOrDefault(defaultProps.Typeface.Weight);
                            break;
                        case TagType.FontStyle:
                        case TagType.FontStyleItalic:
                            curStyle = fontStyle.PopOrDefault(defaultProps.Typeface.Style);
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
                lines[lineNum].Elements.Add(new TextElement()
                {
                    Text = token.Text.AsSpan().ToString(),
                    Typeface = new Typeface(curFont, curStyle, curWeight),
                    Size = curSize,
                    Foreground = curColor,
                    Spacing = curSpace,
                    Margin = curMargin,
                });
            }
            else if (token.Type == TokenType.NewLine)
            {
                lineNum++;
            }
            else if (noParse)
            {
                lines[lineNum].Elements.Add(new TextElement()
                {
                    Text = token.ToString(),
                    Typeface = new Typeface(curFont, curStyle, curWeight),
                    Size = curSize,
                    Foreground = curColor,
                    Spacing = curSpace,
                    Margin = curMargin,
                });
            }
        }

        lines.RemoveAll(i =>
        {
            if (i.Elements.Count < 1)
            {
                foreach (TextElement item in i.Elements.AsSpan())
                {
                    item._paint.Dispose();
                }
                return true;
            }
            else
            {
                return false;
            }
        });

        return lines;
    }
}
