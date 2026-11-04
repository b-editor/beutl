using System.Buffers;
using System.Globalization;
using System.Text;
using Beutl.Media.TextFormatting;

namespace Beutl.Editor.Services.Captions;

internal static class CaptionTextUtilities
{
    // The tags of the subtitle formats, which keep the look they have: a text object draws nothing for those it does
    // not know. SRT cannot mark one as words, and WebVTT's decoded text does not say whether it did.
    private static readonly string[] s_subtitleTags = ["b", "i", "u", "s", "font", "br", "c", "v", "lang", "ruby", "rt"];

    // What a text object holds for a cue's text. It reads angle brackets as tags and draws nothing for one it does not
    // know, so any other tag goes in as the words it is. The cue keeps its text, which also goes to exports and translation.
    public static string ToTextObjectText(string text)
    {
        if (!text.Contains('<')) return text;

        var tokenizer = new FormattedTextTokenizer(text);
        tokenizer.Tokenize();
        var builder = new StringBuilder(text.Length);
        bool noParse = false;
        foreach (FormattedTextTokenizer.Token token in tokenizer.Result)
        {
            if (noParse || token.Type == FormattedTextTokenizer.TokenType.Content || IsFormattingTag(token))
            {
                builder.Append(token.Text.AsSpan());
            }
            else
            {
                builder.Append("<noparse>").Append(token.Text.AsSpan()).Append("</noparse>");
            }

            // Tags inside an existing <noparse> are already words, and one more around them would be drawn too.
            if (token.Type == FormattedTextTokenizer.TokenType.TagStart)
                noParse |= FormattedTextParser.TryParseTag(token.Text, out FormattedTextParser.TagInfo tag)
                           && tag.Type == FormattedTextParser.TagType.NoParse;
            else if (token.Type == FormattedTextTokenizer.TokenType.TagClose)
                noParse &= FormattedTextParser.GetCloseTagType(token.Text) != FormattedTextParser.TagType.NoParse;
        }

        return builder.ToString();
    }

    private static bool IsFormattingTag(FormattedTextTokenizer.Token token)
    {
        bool known = token.Type == FormattedTextTokenizer.TokenType.TagClose
            ? FormattedTextParser.GetCloseTagType(token.Text) != FormattedTextParser.TagType.Invalid
            : FormattedTextParser.TryParseTag(token.Text, out FormattedTextParser.TagInfo tag)
              && tag.Type != FormattedTextParser.TagType.Invalid;
        return known || IsSubtitleTag(token.Text.AsSpan());
    }

    private static bool IsSubtitleTag(ReadOnlySpan<char> tag)
    {
        ReadOnlySpan<char> name = tag[1..^1].TrimStart('/');
        int end = name.IndexOfAny(" \t\n.=/");
        if (end >= 0) name = name[..end];

        foreach (string subtitleTag in s_subtitleTags)
        {
            if (name.Equals(subtitleTag, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static string[] GetLines(string value)
        => NormalizeLineEndings(value).Split('\n');

    public static int GetTextElementCount(string value)
        => StringInfo.ParseCombiningCharacters(value).Length;

    public static bool IsTextElementWhiteSpace(string value, int start, int end)
    {
        ReadOnlySpan<char> element = value.AsSpan(start, end - start);
        OperationStatus status = Rune.DecodeFromUtf16(element, out Rune rune, out int consumed);
        return status == OperationStatus.Done
               && consumed == element.Length
               && Rune.IsWhiteSpace(rune);
    }
}
