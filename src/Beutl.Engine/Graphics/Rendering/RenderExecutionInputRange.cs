using System.Diagnostics;

namespace Beutl.Graphics.Rendering;

/// <summary>Identifies one authored input handle's contiguous values in a flattened execution session.</summary>
/// <param name="StartIndex">The zero-based index of the first value in the session's input list.</param>
/// <param name="Count">The number of runtime values produced by the authored input handle.</param>
public readonly record struct RenderExecutionInputRange(int StartIndex, int Count)
{
    /// <summary>Gets the exclusive end index in the session's input list.</summary>
    public int EndIndex => checked(StartIndex + Count);

    // The executor builds both lists afresh for each session, so a gap or an overlap is an engine bug.
    [Conditional("DEBUG")]
    internal static void AssertCover(int inputCount, IList<RenderExecutionInputRange> inputRanges)
    {
        int expectedStart = 0;
        foreach (RenderExecutionInputRange range in inputRanges)
        {
            Debug.Assert(range.StartIndex == expectedStart && range.Count >= 0);
            expectedStart = range.EndIndex;
        }

        Debug.Assert(expectedStart == inputCount);
    }
}
