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
    {
        if (!grid.IsEnabled || !(grid.Bpm > 0) || !(scale > 0)) return null;

        double beat = 60 / grid.Bpm;
        double beatPixels = beat * FrameNumberHelper.SecondWidth * scale;
        if (!(beatPixels >= MinBeatSpacing)) return null;

        int subdivisions = Math.Max(1, grid.Subdivisions);
        return subdivisions > 1 && beatPixels / subdivisions >= MinSubdivisionSpacing ? beat / subdivisions : beat;
    }
}
