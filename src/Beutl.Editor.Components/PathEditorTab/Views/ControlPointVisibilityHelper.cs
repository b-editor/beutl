using Avalonia.Controls;

using Beutl.Media;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal static class ControlPointVisibilityHelper
{
    public static void Update(
        Panel canvas,
        PathSegment? selectedOperation,
        PathFigure? pathFigure,
        bool isClosed)
    {
        Control[] controlPoints = canvas.Children.Where(i => i.Classes.Contains("control")).ToArray();
        foreach (Control item in controlPoints)
        {
            item.IsVisible = false;
        }

        if (selectedOperation is { } op && pathFigure is { } figure)
        {
            int index = figure.Segments.IndexOf(op);
            int nextIndex = (index + 1) % figure.Segments.Count;

            if (isClosed || index != 0)
            {
                foreach (Control? item in controlPoints.Where(v => v.DataContext == op))
                {
                    if (Equals(item.Tag, "ControlPoint2") || Equals(item.Tag, "ControlPoint"))
                    {
                        item.IsVisible = true;
                    }
                }
            }

            if (isClosed || nextIndex != 0)
            {
                if (0 <= nextIndex && nextIndex < figure.Segments.Count)
                {
                    PathSegment next = figure.Segments[nextIndex];
                    foreach (Control? item in controlPoints.Where(v => v.DataContext == next))
                    {
                        if (Equals(item.Tag, "ControlPoint1") || Equals(item.Tag, "ControlPoint"))
                            item.IsVisible = true;
                    }
                }
            }
        }
    }
}
