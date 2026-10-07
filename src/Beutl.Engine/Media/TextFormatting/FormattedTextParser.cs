using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Beutl.Utilities;

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
        ReadOnlySpan<char> span = TrimAngleBrackets(tag.AsSpan());
        return span.StartsWith("/", StringComparison.Ordinal)
            ? ClassifyTagName(span[1..])
            : TagType.Invalid;
    }

    // 開始タグの種類を取得
    public static TagType GetTagType(StringSpan tag)
    {
        return ClassifyTagName(TrimAngleBrackets(tag.AsSpan()));
    }

    private static ReadOnlySpan<char> TrimAngleBrackets(ReadOnlySpan<char> span)
    {
        if (span.StartsWith("<", StringComparison.Ordinal))
        {
            span = span[1..];
        }
        if (span.EndsWith(">", StringComparison.Ordinal))
        {
            span = span[..^1];
        }

        return span;
    }

    // Opening and closing tags share their names; a closing tag is the same name behind a "/".
    private static TagType ClassifyTagName(ReadOnlySpan<char> name)
    {
        if (name.SequenceEqual("font"))
        {
            return TagType.Font;
        }
        else if (name.SequenceEqual("size"))
        {
            return TagType.Size;
        }
        else if (name.SequenceEqual("color"))
        {
            return TagType.Color;
        }
        else if (name.SequenceEqual("stroke"))
        {
            return TagType.Stroke;
        }
        else if (name.SequenceEqual("cspace"))
        {
            return TagType.CharSpace;
        }
        else if (name.SequenceEqual("weight"))
        {
            return TagType.FontWeight;
        }
        else if (name.SequenceEqual("style"))
        {
            return TagType.FontStyle;
        }
        else if (name.SequenceEqual("b") || name.SequenceEqual("bold"))
        {
            return TagType.FontWeightBold;
        }
        else if (name.SequenceEqual("i") || name.SequenceEqual("italic"))
        {
            return TagType.FontStyleItalic;
        }
        else if (name.SequenceEqual("noparse"))
        {
            return TagType.NoParse;
        }
        else if (name.SequenceEqual("single-line"))
        {
            return TagType.SingleLine;
        }
        else if (name.StartsWith("#", StringComparison.Ordinal))
        {
            return TagType.ColorHash;
        }

        return TagType.Invalid;
    }

    public readonly record struct TagInfo(StringSpan Value, TagType Type)
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

        public bool TryGetStroke([NotNullWhen(true)] out Pen.Resource? pen)
        {
            if (Type is not TagType.Stroke)
            {
                pen = default;
                return false;
            }

            ReadOnlySpan<char> str = RemoveQuotation(Value);
            var tokenizer = new RefStringTokenizer(str);
            if (!tokenizer.TryReadString(out ReadOnlySpan<char> colorStr)
                || !Color.TryParse(colorStr, out Color color)
                || !tokenizer.TryReadSingle(out float thickness))
            {
                pen = default;
                return false;
            }

            StrokeCap cap = StrokeCap.Flat;
            StrokeJoin join = StrokeJoin.Miter;
            StrokeAlignment align = StrokeAlignment.Center;

            ReadOnlySpan<char> str1 = tokenizer.TryReadString(out ReadOnlySpan<char> _str1) ? _str1 : default;
            ReadOnlySpan<char> str2 = tokenizer.TryReadString(out ReadOnlySpan<char> _str2) ? _str2 : default;
            ReadOnlySpan<char> str3 = tokenizer.TryReadString(out ReadOnlySpan<char> _str3) ? _str3 : default;

            ApplyStrokeOption(str1, ref cap, ref join, ref align);
            ApplyStrokeOption(str2, ref cap, ref join, ref align);
            ApplyStrokeOption(str3, ref cap, ref join, ref align);

            float miterLimit = tokenizer.TryReadSingle(out float _miterLimit) ? _miterLimit : 10;

            var brush = new SolidColorBrush.Resource { Color = color, Opacity = 100 };
            pen = new Pen.Resource
            {
                Brush = brush,
                DashArray = null,
                DashOffset = 0,
                Thickness = thickness,
                MiterLimit = miterLimit,
                StrokeCap = cap,
                StrokeJoin = join,
                StrokeAlignment = align,
                TrimEnd = 100,
            };

            return true;
        }

        // Each optional token names a cap, a join or an alignment; the first kind it parses as wins.
        private static void ApplyStrokeOption(
            ReadOnlySpan<char> option,
            ref StrokeCap cap,
            ref StrokeJoin join,
            ref StrokeAlignment align)
        {
            _ = TryReadStrokeCap(option, ref cap)
                || TryReadStrokeJoin(option, ref join)
                || TryReadStrokeAlignment(option, ref align);
        }

        private static bool TryReadStrokeCap(ReadOnlySpan<char> s, ref StrokeCap cap)
        {
            if (s.StartsWith("Join:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.Equals(nameof(StrokeCap.Flat), StringComparison.OrdinalIgnoreCase))
            {
                cap = StrokeCap.Flat;
                return true;
            }
            else if (s.Equals(nameof(StrokeCap.Round), StringComparison.OrdinalIgnoreCase))
            {
                cap = StrokeCap.Round;
                return true;
            }
            else if (s.Equals(nameof(StrokeCap.Square), StringComparison.OrdinalIgnoreCase))
            {
                cap = StrokeCap.Square;
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool TryReadStrokeJoin(ReadOnlySpan<char> s, ref StrokeJoin r)
        {
            if (s.StartsWith("Cap:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.Equals(nameof(StrokeJoin.Miter), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeJoin.Miter;
                return true;
            }
            else if (s.Equals(nameof(StrokeJoin.Round), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeJoin.Round;
                return true;
            }
            else if (s.Equals(nameof(StrokeJoin.Bevel), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeJoin.Bevel;
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool TryReadStrokeAlignment(ReadOnlySpan<char> s, ref StrokeAlignment r)
        {
            if (s.Equals(nameof(StrokeAlignment.Center), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeAlignment.Center;
                return true;
            }
            else if (s.Equals(nameof(StrokeAlignment.Inside), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeAlignment.Inside;
                return true;
            }
            else if (s.Equals(nameof(StrokeAlignment.Outside), StringComparison.OrdinalIgnoreCase))
            {
                r = StrokeAlignment.Outside;
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryGetFont(out FontFamily? font)
        {
            if (Type == TagType.Font)
            {
                ReadOnlySpan<char> str = RemoveQuotation(Value);
                font = new FontFamily(str.ToString());
                if (!FontManager.Instance.FontFamilies.Contains(font))
                {
                    font = null;
                }

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
            ReadOnlySpan<char> span = TrimAngleBrackets(s.AsSpan());
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
        Stroke,
        ColorHash,
        CharSpace,
        FontWeightBold,
        FontWeight,
        FontStyle,
        FontStyleItalic,
        NoParse,
        SingleLine
    }
}
