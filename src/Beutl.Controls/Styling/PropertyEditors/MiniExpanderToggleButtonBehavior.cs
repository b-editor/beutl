using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace Beutl.Controls.Styling.PropertyEditors;

public class MiniExpanderToggleButtonBehavior : Behavior<ContentPresenter>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { })
        {
            AssociatedObject.PointerEntered += OnPointerEntered;
            AssociatedObject.PointerExited += OnPointerExited;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject is { })
        {
            AssociatedObject.PointerEntered -= OnPointerEntered;
            AssociatedObject.PointerExited -= OnPointerExited;
        }
    }

    private void OnPointerExited(object sender, PointerEventArgs e)
    {
        if (AssociatedObject.TemplatedParent is ToggleButton parent)
        {
            (parent.Classes as IPseudoClasses).Set(":pointerover", parent.IsPointerOver);
        }
    }

    private void OnPointerEntered(object sender, PointerEventArgs e)
    {
        if (AssociatedObject.TemplatedParent is ToggleButton parent)
        {
            (parent.Classes as IPseudoClasses).Set(":pointerover", false);
        }
    }
}
