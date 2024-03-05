using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls.PropertyEditors;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.Behaviors;

public class ColorPaletteItemBehavior : Behavior<ColorPaletteItem>
{
    private bool _isPressed;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { })
        {
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject is { })
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }
    }

    private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (AssociatedObject != null && _isPressed)
        {
            _isPressed = false;

            SimpleColorPickerFlyoutPresenter fp = AssociatedObject.FindAncestorOfType<SimpleColorPickerFlyoutPresenter>();
            if (fp?.Content is SimpleColorPicker cp)
            {
                cp.SetColor(AssociatedObject.Color);
                e.Handled = true;
                return;
            }

            BrushEditorFlyoutPresenter fp2 = AssociatedObject.FindAncestorOfType<BrushEditorFlyoutPresenter>();
            if (fp2 != null)
            {
                fp2.SetColorPaletteItem(AssociatedObject.Color);
                e.Handled = true;
            }
        }
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject != null
            && e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
        {
            _isPressed = true;
            e.Handled = true;
        }
    }
}
