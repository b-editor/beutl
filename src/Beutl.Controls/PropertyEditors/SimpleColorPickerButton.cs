using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.Core;

namespace Beutl.Controls.PropertyEditors;

// A swatch that edits its color with SimpleColorPicker, palettes included, for a color that is not a property of an
// object, such as a layer's or a setting's. The picker applies its color when it is confirmed, and ColorConfirmed
// reports a change, which also joins the recent colors.
public class SimpleColorPickerButton : TemplatedControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<SimpleColorPickerButton, Color>(
            nameof(Color), Colors.White, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsAlphaEnabledProperty =
        SimpleColorPicker.IsAlphaEnabledProperty.AddOwner<SimpleColorPickerButton>();

    public static readonly DirectProperty<SimpleColorPickerButton, IBrush> PreviewBrushProperty =
        AvaloniaProperty.RegisterDirect<SimpleColorPickerButton, IBrush>(nameof(PreviewBrush), o => o.PreviewBrush);

    private Button? _button;
    private SimpleColorPickerFlyout? _flyout;
    private IBrush _previewBrush = new SolidColorBrush(Colors.White);

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    // False keeps the color opaque and hides the picker's opacity.
    public bool IsAlphaEnabled
    {
        get => GetValue(IsAlphaEnabledProperty);
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    // The swatch the theme shows for the color.
    public IBrush PreviewBrush => _previewBrush;

    public event TypedEventHandler<SimpleColorPickerButton, (Color OldValue, Color NewValue)>? ColorConfirmed;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_button != null)
        {
            _button.Click -= OnButtonClick;
        }

        base.OnApplyTemplate(e);
        _button = e.NameScope.Find<Button>("PART_Button");
        if (_button != null)
        {
            _button.Click += OnButtonClick;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
        {
            SetAndRaise(PreviewBrushProperty, ref _previewBrush, new SolidColorBrush(Color));
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        // The picker does not light-dismiss, so the button that opened it closes it again.
        if (_flyout is { IsOpen: true })
        {
            _flyout.Hide();
            return;
        }

        if (_flyout == null)
        {
            _flyout = new SimpleColorPickerFlyout { Placement = PlacementMode.Bottom };
            _flyout.Confirmed += OnFlyoutConfirmed;
        }

        _flyout.ColorPicker.IsAlphaEnabled = IsAlphaEnabled;
        _flyout.ColorPicker.Color = Color;
        _flyout.ShowAt(this);
    }

    private void OnFlyoutConfirmed(DraggablePickerFlyout<SimpleColorPicker> sender, EventArgs args)
    {
        Color oldColor = Color;
        Color newColor = sender.ColorPicker.Color;
        if (newColor != oldColor)
        {
            // A bound color keeps its binding, which a one-way binding would lose to a local value.
            SetCurrentValue(ColorProperty, newColor);
            ColorConfirmed?.Invoke(this, (oldColor, newColor));
            ColorPaletteService.Instance.AddRecentColor(newColor);
        }
    }
}
