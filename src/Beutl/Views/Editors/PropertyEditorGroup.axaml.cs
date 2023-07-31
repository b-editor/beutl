using Avalonia.Controls;
using Avalonia.Interactivity;

using static Beutl.Views.Editors.PropertiesEditor;

namespace Beutl.Views.Editors;

public partial class PropertyEditorGroup : UserControl
{
    private bool _pressed;

    public PropertyEditorGroup()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();

        separator.PointerPressed += OnSeparatorPointerPressed;
        separator.PointerReleased += OnSeparatorPointerReleased;
    }

    private void OnSeparatorPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_pressed)
        {
            if (properties.IsVisible)
                Hide();
            else
                Show();

            _pressed = false;
        }
    }

    private void OnSeparatorPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(separator).Properties.IsLeftButtonPressed
            && e.ClickCount == 2)
        {
            _pressed = true;
        }
    }

    private void ShowClick(object? sender, RoutedEventArgs e)
    {
        Show();
    }

    private void Hide()
    {
        properties.IsVisible = false;
        summary.IsVisible = true;
    }

    private void Show()
    {
        properties.IsVisible = true;
        summary.IsVisible = false;
    }
}
