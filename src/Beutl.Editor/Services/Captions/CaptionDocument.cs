using System.Collections.ObjectModel;
using System.Globalization;

namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Owns an editable ordered collection of caption cues.
/// </summary>
public sealed class CaptionDocument
{
    private readonly List<CaptionCue> _cues;
    private readonly ReadOnlyCollection<CaptionCue> _readOnlyCues;

    public CaptionDocument()
        : this([])
    {
    }

    public CaptionDocument(IEnumerable<CaptionCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);

        _cues = [];
        foreach (CaptionCue cue in cues)
        {
            ArgumentNullException.ThrowIfNull(cue);
            _cues.Add(cue);
        }

        _readOnlyCues = _cues.AsReadOnly();
    }

    public IReadOnlyList<CaptionCue> Cues => _readOnlyCues;

    public int Count => _cues.Count;

    public CaptionCue this[int index] => _cues[index];

    public void Add(CaptionCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        _cues.Add(cue);
    }

    public void Insert(int index, CaptionCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        _cues.Insert(index, cue);
    }

    public void Replace(int index, CaptionCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        _cues[index] = cue;
    }

    public CaptionCue RemoveAt(int index)
    {
        CaptionCue cue = _cues[index];
        _cues.RemoveAt(index);
        return cue;
    }

    /// <summary>
    /// Splits a cue at a timeline position and UTF-16 text offset while retaining its metadata.
    /// </summary>
    public (CaptionCue First, CaptionCue Second) SplitCue(
        int cueIndex,
        TimeSpan splitTime,
        int textOffset)
    {
        CaptionCue cue = GetCue(cueIndex);
        if (splitTime <= cue.Start || splitTime >= cue.End)
        {
            throw new ArgumentOutOfRangeException(
                nameof(splitTime),
                splitTime,
                "The split time must be strictly inside the cue interval.");
        }

        if ((uint)textOffset > (uint)cue.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(textOffset));
        }

        if (!IsTextElementBoundary(cue.Text, textOffset))
        {
            throw new ArgumentException(
                "The text offset must not split a Unicode text element.",
                nameof(textOffset));
        }

        var first = cue with
        {
            End = splitTime,
            Text = cue.Text[..textOffset],
        };
        var second = cue with
        {
            Start = splitTime,
            Text = cue.Text[textOffset..],
        };

        _cues[cueIndex] = first;
        _cues.Insert(cueIndex + 1, second);
        return (first, second);
    }

    /// <summary>
    /// Merges a cue with the cue immediately after it.
    /// Metadata is retained only when both cues agree on its value.
    /// </summary>
    public CaptionCue MergeWithNext(int firstCueIndex, string separator = "\n")
    {
        ArgumentNullException.ThrowIfNull(separator);
        CaptionCue first = GetCue(firstCueIndex);
        if (firstCueIndex == _cues.Count - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstCueIndex),
                firstCueIndex,
                "The last cue has no following cue to merge.");
        }

        CaptionCue second = _cues[firstCueIndex + 1];
        var merged = new CaptionCue(
            Min(first.Start, second.Start),
            Max(first.End, second.End),
            first.Text + separator + second.Text,
            MergeMetadata(first.Speaker, second.Speaker),
            MergeMetadata(first.Language, second.Language),
            first.Metadata.RetainMatching(second.Metadata));

        _cues[firstCueIndex] = merged;
        _cues.RemoveAt(firstCueIndex + 1);
        return merged;
    }

    private CaptionCue GetCue(int index)
    {
        if ((uint)index >= (uint)_cues.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _cues[index];
    }

    private static bool IsTextElementBoundary(string text, int offset)
    {
        if (offset == 0 || offset == text.Length)
            return true;

        return Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), offset) >= 0;
    }

    private static string? MergeMetadata(string? first, string? second)
        => string.Equals(first, second, StringComparison.Ordinal) ? first : null;

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first <= second ? first : second;

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;
}
