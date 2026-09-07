namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Defines line-length and line-count constraints for caption text.
/// </summary>
public sealed record CaptionTextConstraints
{
    public CaptionTextConstraints(int maximumLineLength = 42, int maximumLineCount = 2)
    {
        if (maximumLineLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLineLength));

        if (maximumLineCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLineCount));

        MaximumLineLength = maximumLineLength;
        MaximumLineCount = maximumLineCount;
    }

    /// <summary>
    /// Gets the maximum number of Unicode text elements on one line.
    /// </summary>
    public int MaximumLineLength { get; }

    public int MaximumLineCount { get; }
}
