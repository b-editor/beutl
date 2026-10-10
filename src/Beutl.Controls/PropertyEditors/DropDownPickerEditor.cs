using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

/// <summary>
/// A property editor that shows a value as text on a drop-down button, laid out as the font family editor
/// is, and leaves picking a new value to whoever handles <see cref="DropDownClick"/>.
/// </summary>
public class DropDownPickerEditor : PropertyEditor
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<DropDownPickerEditor, string?>(nameof(Text));

    public static readonly RoutedEvent<RoutedEventArgs> DropDownClickEvent =
        RoutedEvent.Register<DropDownPickerEditor, RoutedEventArgs>(nameof(DropDownClick), RoutingStrategies.Bubble);

    private IDisposable? _disposable;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? DropDownClick
    {
        add => AddHandler(DropDownClickEvent, value);
        remove => RemoveHandler(DropDownClickEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposable?.Dispose();
        base.OnApplyTemplate(e);
        var button = e.NameScope.Get<DropDownButton>("PART_InnerButton");
        _disposable = button.AddDisposableHandler(Button.ClickEvent, OnButtonClick);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(DropDownClickEvent, this));
    }
}
