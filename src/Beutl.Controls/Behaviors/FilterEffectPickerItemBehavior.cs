using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Controls.Behaviors;

public class FilterEffectPickerItemBehavior : Behavior<ToggleButton>
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
        var parent = AssociatedObject?.FindAncestorOfType<FilterEffectPickerFlyoutPresenter>();
        if (parent != null && AssociatedObject is { DataContext: PinnableLibraryItem item })
        {
            parent.UpdatePinState(item, AssociatedObject.IsChecked == true);
        }
    }
}
