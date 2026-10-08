using Avalonia.Controls;
using FluentAvalonia.UI.Media;

namespace FluentAvalonia.UI.Controls;

/// <summary>
/// Defines the base class for all component controls of a <see cref="FAColorPicker"/>
/// </summary>
public abstract partial class ColorPickerComponent : Control
{
    static ColorPickerComponent()
    {
        FocusableProperty.OverrideDefaultValue<ColorPickerComponent>(true);
    }

    protected virtual void OnColorChanged(Color2 oldColor, Color2 newColor)
    {
        ColorChanged?.Invoke(this, new ColorChangedEventArgs(newColor));
    }

    protected virtual void OnComponentChanged(ColorComponent newValue)
    {
        InvalidateVisual();
    }
}
