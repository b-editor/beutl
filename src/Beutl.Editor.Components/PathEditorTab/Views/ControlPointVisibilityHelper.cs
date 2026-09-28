using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Beutl.Composition;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Media;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal static class ControlPointVisibilityHelper
{
    public static void Update(
        Panel canvas,
        PathSegment? selectedOperation,
        PathFigure? pathFigure,
        CompositionContext context)
    {
        Control[] controlPoints = canvas.Children.Where(i => i.Classes.Contains("control")).ToArray();
        foreach (Control item in controlPoints)
        {
            item.IsVisible = false;
        }

        if (pathFigure is not { Segments.Count: > 0 } figure) return;
        Thumb[] anchors = canvas.Children.OfType<Thumb>().Where(t => !t.Classes.Contains("control")).ToArray();
        if (selectedOperation == null)
        {
            foreach (Thumb anchor in anchors) PathPointDragBehavior.SetIsSelected(anchor, false);
        }
        else if (!anchors.Any(t => ReferenceEquals(t.DataContext, selectedOperation) && PathPointDragBehavior.GetIsSelected(t)))
        {
            foreach (Thumb anchor in anchors)
                PathPointDragBehavior.SetIsSelected(anchor, ReferenceEquals(anchor.DataContext, selectedOperation));
        }
        var selected = canvas.Children.OfType<Thumb>()
            .Where(t => !t.Classes.Contains("control") && PathPointDragBehavior.GetIsSelected(t))
            .Select(t => t.DataContext).OfType<PathSegment>().ToHashSet();
        if (selectedOperation != null) selected.Add(selectedOperation);
        foreach (var anchor in selected)
        {
            foreach (var property in PathPointProperties.Get(figure, anchor, context))
            {
                if (property.Role == PathPointPropertyRole.Position) continue;
                foreach (Thumb thumb in controlPoints.OfType<Thumb>())
                {
                    if (ReferenceEquals(thumb.DataContext, property.Owner)
                        && ReferenceEquals(PathEditorHelper.GetProperty(thumb), property.Property))
                        thumb.IsVisible = true;
                }
            }
        }
    }
}
