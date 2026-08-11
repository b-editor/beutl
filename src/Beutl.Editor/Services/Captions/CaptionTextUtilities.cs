using System.Buffers;
using System.Globalization;
using System.Text;

namespace Beutl.Editor.Services.Captions;

internal static class CaptionTextUtilities
{
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
