using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.Captions;

internal static class SceneCaptionExporter
{
    public static (CaptionDocument Document, int SkippedCount) BuildDocument(IEnumerable<Element> elements)
    {
        var cues = new List<CaptionCue>();
        int skipped = 0;
        foreach (Element element in elements)
        {
            TextBlock[] blocks = CaptionTextUtilities.FindTextBlocks(element).ToArray();
            if (blocks.Length == 0)
            {
                skipped++;
                continue;
            }

            // Templates can repeat the cue in several text objects within the same clip.
            string text = string.Join('\n', blocks
                .Select(block => CaptionTextUtilities.ToSubtitleText(block.Text.Value ?? string.Empty))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal));
            TimeRange range = element.Range;
            cues.Add(new CaptionCue(range.Start.ToTimeSpan(), range.End.ToTimeSpan(), text));
        }

        return (new CaptionDocument(cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.End)), skipped);
    }
}
