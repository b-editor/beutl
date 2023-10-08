using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.PropertyEditors;

public class ColorEditor : PropertyEditor
{
    public static readonly DirectProperty<ColorEditor, Color> ValueProperty =
        AvaloniaProperty.RegisterDirect<ColorEditor, Color>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLivePreviewEnabledProperty =
        AvaloniaProperty.Register<ColorEditor, bool>(nameof(IsLivePreviewEnabled));

    private Color _oldValue;
    private Color _value;

    public Color Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    public bool IsLivePreviewEnabled
    {
        get => GetValue(IsLivePreviewEnabledProperty);
        set => SetValue(IsLivePreviewEnabledProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ColorPickerButton button = e.NameScope.Get<ColorPickerButton>("PART_ColorPickerButton");
        button.ColorChanged += OnColorChanged;
        button.FlyoutOpened += OnFlyoutOpened;
        button.FlyoutClosed += OnFlyoutClosed;
        button.FlyoutConfirmed += OnFlyoutConfirmed;
    }

    private void OnFlyoutOpened(ColorPickerButton sender, EventArgs args)
    {
        if (IsLivePreviewEnabled)
            _oldValue = _value;
    }

    private void OnFlyoutClosed(ColorPickerButton sender, EventArgs args)
    {
        if (IsLivePreviewEnabled)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                Value, _oldValue, ValueConfirmedEvent));
        }
    }

    private void OnFlyoutConfirmed(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        if (!IsLivePreviewEnabled)
        {
            Value = args.NewColor.GetValueOrDefault();
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                Value, args.OldColor.GetValueOrDefault(), ValueConfirmedEvent));
        }
    }

    private void OnColorChanged(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        if (IsLivePreviewEnabled)
        {
            Value = args.NewColor.GetValueOrDefault();
            RaiseEvent(new PropertyEditorValueChangedEventArgs<Color>(
                args.NewColor.GetValueOrDefault(), args.OldColor.GetValueOrDefault(), ValueChangedEvent));
        }
    }
}
