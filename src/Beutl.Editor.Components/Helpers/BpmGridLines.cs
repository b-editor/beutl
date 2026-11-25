using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

// Which lines of the BPM grid show at a timeline scale. BpmGridOverlay draws these, and the timeline's drags snap to
// the same ones, so a drag never stops on a line that is not drawn.
public static class BpmGridLines
{
    // Beats any closer would merge into a fill, and subdivisions any closer would blur the beats between them.
    public const double MinBeatSpacing = 1;
    public const double MinSubdivisionSpacing = 3;

    // The seconds between neighbouring lines that show: a subdivision's while subdivisions show, else a beat's. Null
    // while the grid is off or its beats are too close to draw.
    public static double? GetShownInterval(BpmGridOptions grid, float scale)
        => GetShownSpacing(grid, scale) is { } spacing ? 60 / grid.Bpm / spacing.Parts : null;

    // The pixels between beats and the parts a beat shows in, its subdivisions while they show, else 1; null as above.
    public static (double BeatPixels, int Parts)? GetShownSpacing(BpmGridOptions grid, float scale)
    {
        if (!grid.IsEnabled || !(grid.Bpm > 0) || !(scale > 0)) return null;

        double beatPixels = 60 / grid.Bpm * FrameNumberHelper.SecondWidth * scale;
        if (!(beatPixels >= MinBeatSpacing)) return null;

        int subdivisions = Math.Max(1, grid.Subdivisions);
        return (beatPixels, subdivisions > 1 && beatPixels / subdivisions >= MinSubdivisionSpacing ? subdivisions : 1);
    }
}
