using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Controls.Behaviors;

public class OutputPickerItemMoreButtonBehavior : Behavior<Button>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.Click += OnClick;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.Click -= OnClick;
        }
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var parent = AssociatedObject?.FindAncestorOfType<OutputPickerFlyoutPresenter>();
        if (parent != null && AssociatedObject is { DataContext: PinnableOutputItem item })
        {
            parent.RequestMoreMenu(item, AssociatedObject);
        }
    }
}
