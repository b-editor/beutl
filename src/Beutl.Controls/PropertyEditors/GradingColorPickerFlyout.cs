using Avalonia.Controls;

using Beutl.Media;

using FluentAvalonia.Core;

namespace Beutl.Controls.PropertyEditors;

internal sealed class GradingColorPickerFlyout : DraggablePickerFlyout<GradingColorPicker>
{
    public GradingColor Color
    {
        get => ColorPicker.Color;
        set => ColorPicker.Color = value;
    }

    public event TypedEventHandler<GradingColorPickerFlyout, (GradingColor OldValue, GradingColor NewValue)>? ColorChanged;

    protected override Control CreatePresenter()
    {
        Control presenter = base.CreatePresenter();
        ColorPicker.ColorChanged += OnPickerColorChanged;
        return presenter;
    }

    private void OnPickerColorChanged(GradingColorPicker sender, (GradingColor OldValue, GradingColor NewValue) args)
    {
        ColorChanged?.Invoke(this, args);
    }
}
