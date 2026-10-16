using Avalonia;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

internal class GradingColorEditor : ColorPickerEditorBase<GradingColor, GradingColorPicker, GradingColorPickerFlyout>
{
    public GradingColorEditor()
        : base(default, CreateBrush(default))
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            Flyout?.Color = Value;
        }
    }

    private static Avalonia.Media.SolidColorBrush CreateBrush(GradingColor color) => new(color.ToColor().ToAvaColor());

    private protected override Avalonia.Media.IBrush CreatePreviewBrush(GradingColor value) => CreateBrush(value);

    private protected override GradingColor GetFlyoutColor(GradingColorPickerFlyout flyout) => flyout.Color;

    private protected override void SetFlyoutColor(GradingColorPickerFlyout flyout, GradingColor color) => flyout.Color = color;

    private protected override void SetLivePreview(GradingColorPickerFlyout flyout, bool enabled)
    {
        if (enabled)
            flyout.ColorChanged += OnFlyoutColorChanged;
        else
            flyout.ColorChanged -= OnFlyoutColorChanged;
    }

    // The editor's value before the change is the old value, rather than the picker's.
    private void OnFlyoutColorChanged(GradingColorPickerFlyout sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        OnLiveColorChanged(args.NewValue, Value);
    }
}
