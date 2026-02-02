using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Editor.Components.Views;

public static class TimelineSharedObject
{
    public static readonly IPen SelectionPen;
    public static readonly IBrush SelectionFillBrush = new ImmutableSolidColorBrush(Colors.CornflowerBlue, 0.3);

    static TimelineSharedObject()
    {
        SelectionPen = new ImmutablePen(Brushes.CornflowerBlue, 0.5);
    }
}
