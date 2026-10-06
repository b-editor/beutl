using System.Collections;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace Beutl.Views;

// Shared by the behaviors that reorder list items by dragging them.
internal static class ReorderDragHelper
{
    // Moves every realized container back to its layout position.
    public static void ResetTranslateTransforms(ItemsControl itemsControl, IEnumerable items)
    {
        int i = 0;

        foreach (object? _ in items)
        {
            Control? container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    public static void SetDraggingPseudoClasses(Control control, bool isDragging)
    {
        ((IPseudoClasses)control.Classes).Set(":dragging", isDragging);
    }

    public static void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}
