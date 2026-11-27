using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.Captions;

internal static class SceneCaptionExporter
{
    public static (CaptionDocument Document, int SkippedCount) BuildDocument(
        IEnumerable<Element> elements, CaptionFormatId format)
    {
        var cues = new List<CaptionCue>();
        int skipped = 0;
        foreach (Element element in elements)
        {
            TimeRange range = element.Range;
            string text = ExtractText(element, range.Start);
            if (text.Length == 0)
            {
                skipped++;
                continue;
            }

            if (format == CaptionFormats.Srt)
                text = CaptionTextUtilities.ProtectSrtLiteralTags(text);

            cues.Add(new CaptionCue(range.Start.ToTimeSpan(), range.End.ToTimeSpan(), text));
        }

        return (new CaptionDocument(cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.End)), skipped);
    }

    internal static string ExtractText(Element element, MediaTime start)
    {
        var context = new EvaluationContext(start);
        // Templates can repeat the cue in several text objects within the same clip.
        return string.Join('\n', CaptionTextUtilities.FindTextBlocks(element)
            .Select(block => CaptionTextUtilities.ToSubtitleText(block.Text.Evaluate(context) ?? string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal));
    }
}
