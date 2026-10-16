using Avalonia.Media;

using FluentAvalonia.UI.Media;

namespace Beutl.Controls.PropertyEditors;

public class ColorEditor : ColorPickerEditorBase<Color, SimpleColorPicker, SimpleColorPickerFlyout>
{
    public ColorEditor()
        : base(Colors.White, new SolidColorBrush(Colors.White))
    {
    }

    private protected override IBrush CreatePreviewBrush(Color value) => new SolidColorBrush(value);

    private protected override Color GetFlyoutColor(SimpleColorPickerFlyout flyout) => flyout.ColorPicker.Color;

    private protected override void SetFlyoutColor(SimpleColorPickerFlyout flyout, Color color) => flyout.ColorPicker.Color = color;

    private protected override void SetLivePreview(SimpleColorPickerFlyout flyout, bool enabled)
    {
        if (enabled)
            flyout.ColorPicker.ColorChanged += OnColorPickerColorChanged;
        else
            flyout.ColorPicker.ColorChanged -= OnColorPickerColorChanged;
    }

    private void OnColorPickerColorChanged(SimpleColorPicker sender, (Color2 OldValue, Color2 NewValue) args)
    {
        OnLiveColorChanged(args.NewValue, args.OldValue);
    }
}
