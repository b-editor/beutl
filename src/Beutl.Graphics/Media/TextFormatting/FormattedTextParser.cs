using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Beutl.Graphics;

namespace Beutl.Media.TextFormatting;

public static class FormattedTextParser
{
    // タグを解析
    public static bool TryParseTag(StringSpan tag, out TagInfo result)
    {
        ReadOnlySpan<char> span = tag.AsSpan();
        if (span.IsWhiteSpace())
        {
            result = default;
            return false;
        }

        int assignIdx = span.IndexOf("=", StringComparison.Ordinal);
        StringSpan first = tag;
        StringSpan second = StringSpan.Empty;
        if (assignIdx >= 0)
        {
            first = tag.Slice(0, assignIdx);
            second = tag.Slice(assignIdx + 1);
        }
        TagType tagType = GetTagType(first);

        // "<#fffff>" みたいなタグ
        if (tagType is TagType.ColorHash or TagType.FontWeightBold or TagType.FontStyleItalic or TagType.NoParse or TagType.SingleLine)
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

    // 終了タグの種類を取得
    public static TagType GetCloseTagType(StringSpan tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan();

        if (span.StartsWith("<", StringComparison.Ordinal))
        {
            span = span[1..];
        }
        if (span.EndsWith(">", StringComparison.Ordinal))
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
        else if (span.SequenceEqual("/b") || span.SequenceEqual("/bold"))
        {
            return TagType.FontWeightBold;
        }
        else if (span.SequenceEqual("/i") || span.SequenceEqual("/italic"))
        {
            return TagType.FontStyleItalic;
        }
        else if (span.SequenceEqual("/noparse"))
        {
            return TagType.NoParse;
        }
        else if (span.SequenceEqual("/single-line"))
        {
            return TagType.SingleLine;
        }
        else if (span.StartsWith("/#", StringComparison.Ordinal))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    // 開始タグの種類を取得
    public static TagType GetTagType(StringSpan tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan();

        if (span.StartsWith("<", StringComparison.Ordinal))
        {
            span = span[1..];
        }
        if (span.EndsWith(">", StringComparison.Ordinal))
        {
            span = span[..^1];
        }

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
        else if (span.SequenceEqual("b") || span.SequenceEqual("bold"))
        {
            return TagType.FontWeightBold;
        }
        else if (span.SequenceEqual("i") || span.SequenceEqual("italic"))
        {
            return TagType.FontStyleItalic;
        }
        else if (span.SequenceEqual("noparse"))
        {
            return TagType.NoParse;
        }
        else if (span.SequenceEqual("single-line"))
        {
            return TagType.SingleLine;
        }
        else if (span.StartsWith("#", StringComparison.Ordinal))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
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

        public bool TryGetFont([NotNullWhen(true)] out FontFamily? font)
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
            ReadOnlySpan<char> span = s.AsSpan();
            if (span.StartsWith("<", StringComparison.Ordinal))
            {
                span = span[1..];
            }
            if (span.EndsWith(">", StringComparison.Ordinal))
            {
                span = span[..^1];
            }
            if (span.StartsWith("\"", StringComparison.Ordinal) ||
                span.StartsWith("\'", StringComparison.Ordinal))
            {
                span = span[1..];
            }
            if (span.EndsWith("\"", StringComparison.Ordinal) ||
               span.EndsWith("\'", StringComparison.Ordinal))
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
        NoParse,
        SingleLine
    }
}
