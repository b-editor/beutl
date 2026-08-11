using System.Globalization;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Wraps caption text without splitting Unicode text elements or truncating content.
/// </summary>
public static class CaptionTextWrapper
{
    public static string Wrap(string text, CaptionTextConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(constraints);

        var output = new List<string>();
        foreach (string line in CaptionTextUtilities.GetLines(text))
        {
            WrapLine(line, constraints.MaximumLineLength, output);
        }

        return string.Join('\n', output);
    }

    /// <summary>
    /// Wraps all content and reports whether the result also fits the maximum line count.
    /// </summary>
    public static bool TryWrap(
        string text,
        CaptionTextConstraints constraints,
        out string wrappedText)
    {
        wrappedText = Wrap(text, constraints);
        return CaptionTextUtilities.GetLines(wrappedText).Length <= constraints.MaximumLineCount;
    }

    private static void WrapLine(string line, int maximumLineLength, List<string> output)
    {
        string remaining = line;
        while (true)
        {
            int[] boundaries = StringInfo.ParseCombiningCharacters(remaining);
            if (boundaries.Length <= maximumLineLength)
            {
                output.Add(remaining);
                return;
            }

            int breakElement = FindWordBreak(remaining, boundaries, maximumLineLength);
            if (breakElement > 0)
            {
                int breakOffset = boundaries[breakElement];
                string segment = remaining[..breakOffset];
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    output.Add(segment);

                    int nextElement = breakElement;
                    while (nextElement < boundaries.Length
                           && IsWhiteSpace(remaining, boundaries, nextElement))
                    {
                        nextElement++;
                    }

                    if (nextElement == boundaries.Length)
                        return;

                    remaining = remaining[boundaries[nextElement]..];
                    continue;
                }
            }

            int hardBreakOffset = boundaries[maximumLineLength];
            output.Add(remaining[..hardBreakOffset]);
            remaining = remaining[hardBreakOffset..];
        }
    }

    private static int FindWordBreak(string value, int[] boundaries, int maximumLineLength)
    {
        int candidate = Math.Min(maximumLineLength, boundaries.Length - 1);
        for (int i = candidate; i > 0; i--)
        {
            if (IsWhiteSpace(value, boundaries, i))
                return i;
        }

        return -1;
    }

    private static bool IsWhiteSpace(string value, int[] boundaries, int elementIndex)
    {
        int start = boundaries[elementIndex];
        int end = elementIndex + 1 < boundaries.Length ? boundaries[elementIndex + 1] : value.Length;
        return CaptionTextUtilities.IsTextElementWhiteSpace(value, start, end);
    }
}
