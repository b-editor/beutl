namespace Beutl.Editor.Services.Captions;

public enum CaptionValidationIssueKind
{
    NegativeStart,
    EndNotAfterStart,
    OutOfOrder,
    Overlap,
    TooManyLines,
    LineTooLong,
}

/// <param name="CueIndex">The zero-based index of the cue containing the issue.</param>
/// <param name="RelatedCueIndex">The zero-based index of a related cue, when applicable.</param>
/// <param name="LineIndex">The zero-based line index, when applicable.</param>
public sealed record CaptionValidationIssue(
    CaptionValidationIssueKind Kind,
    int CueIndex,
    int? RelatedCueIndex = null,
    int? LineIndex = null,
    int? ActualValue = null,
    int? Limit = null);

public static class CaptionDocumentValidator
{
    public static IReadOnlyList<CaptionValidationIssue> Validate(
        CaptionDocument document,
        CaptionTextConstraints? textConstraints = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<CaptionValidationIssue>();
        ValidateTimingAndOrder(document, issues);
        ValidateOverlaps(document, issues);

        if (textConstraints is not null)
            ValidateText(document, textConstraints, issues);

        return issues.AsReadOnly();
    }

    private static void ValidateTimingAndOrder(
        CaptionDocument document,
        List<CaptionValidationIssue> issues)
    {
        for (int i = 0; i < document.Count; i++)
        {
            CaptionCue cue = document[i];
            if (cue.Start < TimeSpan.Zero)
            {
                issues.Add(new CaptionValidationIssue(
                    CaptionValidationIssueKind.NegativeStart,
                    i));
            }

            if (cue.End <= cue.Start)
            {
                issues.Add(new CaptionValidationIssue(
                    CaptionValidationIssueKind.EndNotAfterStart,
                    i));
            }

            if (i > 0 && cue.Start < document[i - 1].Start)
            {
                issues.Add(new CaptionValidationIssue(
                    CaptionValidationIssueKind.OutOfOrder,
                    i,
                    i - 1));
            }
        }
    }

    private static void ValidateOverlaps(
        CaptionDocument document,
        List<CaptionValidationIssue> issues)
    {
        (CaptionCue Cue, int Index)[] ordered = document.Cues
            .Select((cue, index) => (cue, index))
            .Where(item => item.cue.Start >= TimeSpan.Zero && item.cue.End > item.cue.Start)
            .OrderBy(item => item.cue.Start)
            .ThenBy(item => item.cue.End)
            .Select(item => (item.cue, item.index))
            .ToArray();

        if (ordered.Length == 0)
            return;

        TimeSpan greatestEnd = ordered[0].Cue.End;
        int greatestEndIndex = ordered[0].Index;
        for (int i = 1; i < ordered.Length; i++)
        {
            (CaptionCue cue, int index) = ordered[i];
            if (cue.Start < greatestEnd)
            {
                issues.Add(new CaptionValidationIssue(
                    CaptionValidationIssueKind.Overlap,
                    index,
                    greatestEndIndex));
            }

            if (cue.End > greatestEnd)
            {
                greatestEnd = cue.End;
                greatestEndIndex = index;
            }
        }
    }

    private static void ValidateText(
        CaptionDocument document,
        CaptionTextConstraints constraints,
        List<CaptionValidationIssue> issues)
    {
        for (int cueIndex = 0; cueIndex < document.Count; cueIndex++)
        {
            string[] lines = CaptionTextUtilities.GetLines(document[cueIndex].Text);
            if (lines.Length > constraints.MaximumLineCount)
            {
                issues.Add(new CaptionValidationIssue(
                    CaptionValidationIssueKind.TooManyLines,
                    cueIndex,
                    ActualValue: lines.Length,
                    Limit: constraints.MaximumLineCount));
            }

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                int lineLength = CaptionTextUtilities.GetTextElementCount(lines[lineIndex]);
                if (lineLength > constraints.MaximumLineLength)
                {
                    issues.Add(new CaptionValidationIssue(
                        CaptionValidationIssueKind.LineTooLong,
                        cueIndex,
                        LineIndex: lineIndex,
                        ActualValue: lineLength,
                        Limit: constraints.MaximumLineLength));
                }
            }
        }
    }
}
