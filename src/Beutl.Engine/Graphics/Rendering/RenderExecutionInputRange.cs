namespace Beutl.Graphics.Rendering;

/// <summary>Identifies one authored input handle's contiguous values in a flattened execution session.</summary>
/// <param name="StartIndex">The zero-based index of the first value in the session's input list.</param>
/// <param name="Count">The number of runtime values produced by the authored input handle.</param>
public readonly record struct RenderExecutionInputRange(int StartIndex, int Count)
{
    /// <summary>Gets the exclusive end index in the session's input list.</summary>
    public int EndIndex => checked(StartIndex + Count);

    internal static IReadOnlyList<RenderExecutionInputRange> CopyAndValidate(
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        RenderExecutionInputRange[] copiedRanges = inputRanges.ToArray();
        int expectedStart = 0;
        foreach (RenderExecutionInputRange range in copiedRanges)
        {
            if (range.StartIndex != expectedStart || range.Count < 0)
            {
                throw new ArgumentException(
                    "Execution input ranges must be non-negative, contiguous, and in authored order.",
                    parameterName);
            }

            expectedStart = range.EndIndex;
        }

        if (expectedStart != inputs.Count)
        {
            throw new ArgumentException(
                "Execution input ranges must cover every flattened execution input exactly once.",
                parameterName);
        }

        return copiedRanges.Length == 0
            ? copiedRanges
            : Array.AsReadOnly(copiedRanges);
    }
}
