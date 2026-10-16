using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

// The value, picker button, flyout and live-preview wiring that ColorEditor and GradingColorEditor share.
public abstract class ColorPickerEditorBase<TColor, TPicker, TFlyout> : PropertyEditor
    where TPicker : Control, new()
    where TFlyout : DraggablePickerFlyout<TPicker>, new()
{
    public static readonly DirectProperty<ColorPickerEditorBase<TColor, TPicker, TFlyout>, TColor> ValueProperty =
        AvaloniaProperty.RegisterDirect<ColorPickerEditorBase<TColor, TPicker, TFlyout>, TColor>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsLivePreviewEnabledProperty =
        AvaloniaProperty.Register<ColorPickerEditorBase<TColor, TPicker, TFlyout>, bool>(nameof(IsLivePreviewEnabled));

    public static readonly DirectProperty<ColorPickerEditorBase<TColor, TPicker, TFlyout>, IBrush> PreviewBrushProperty =
        AvaloniaProperty.RegisterDirect<ColorPickerEditorBase<TColor, TPicker, TFlyout>, IBrush>(
            nameof(PreviewBrush), o => o.PreviewBrush);

    private TFlyout? _flyout;

    private bool _flyoutActive;
    private Button? _button;

    private TColor _oldValue;
    private TColor _value;
    private IBrush _previewBrush;

    private protected ColorPickerEditorBase(TColor value, IBrush previewBrush)
    {
        _oldValue = value;
        _value = value;
        _previewBrush = previewBrush;
    }

    public TColor Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                SetAndRaise(PreviewBrushProperty, ref _previewBrush, CreatePreviewBrush(value));
            }
        }
    }

    // The swatch the shared ColorEditor theme shows for the value.
    public IBrush PreviewBrush => _previewBrush;

    public bool IsLivePreviewEnabled
    {
        get => GetValue(IsLivePreviewEnabledProperty);
        set => SetValue(IsLivePreviewEnabledProperty, value);
    }

    private protected TFlyout? Flyout => _flyout;

    private protected abstract IBrush CreatePreviewBrush(TColor value);

    private protected abstract TColor GetFlyoutColor(TFlyout flyout);

    private protected abstract void SetFlyoutColor(TFlyout flyout, TColor color);

    // Subscribes to or unsubscribes from the picker's color changes, which call OnLiveColorChanged.
    private protected abstract void SetLivePreview(TFlyout flyout, bool enabled);

    private protected void OnLiveColorChanged(TColor newValue, TColor oldValue)
    {
        if (IsLivePreviewEnabled)
        {
            Value = newValue;
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TColor>(newValue, oldValue, ValueChangedEvent));
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_button != null)
        {
            _button.Click -= OnButtonClick;
        }
        base.OnApplyTemplate(e);
        _button = e.NameScope.Get<Button>("PART_ColorPickerButton");
        _button.Click += OnButtonClick;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _flyout ??= new TFlyout();
        _flyout.Closed += OnFlyoutClosed;
        _flyout.Confirmed += OnFlyoutConfirmed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_flyout != null)
        {
            _flyout.Closed -= OnFlyoutClosed;
            _flyout.Confirmed -= OnFlyoutConfirmed;
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_flyout == null) return;
        _flyout.Hide();

        SetFlyoutColor(_flyout, Value);

        _flyout.Placement = PlacementMode.Bottom;

        _flyout.ShowHideButtons(!IsLivePreviewEnabled);

        if (IsLivePreviewEnabled)
        {
            SetLivePreview(_flyout, true);
        }

        _flyout.ShowAt(this);

        _flyoutActive = true;

        _oldValue = _value;
    }

    private void OnFlyoutConfirmed(DraggablePickerFlyout<TPicker> sender, EventArgs args)
    {
        if (_flyoutActive && _flyout != null)
        {
            Value = GetFlyoutColor(_flyout);
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TColor>(Value, _oldValue, ValueConfirmedEvent));
        }
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        if (_flyout != null && IsLivePreviewEnabled)
        {
            SetLivePreview(_flyout, false);
            RaiseEvent(new PropertyEditorValueChangedEventArgs<TColor>(Value, _oldValue, ValueConfirmedEvent));
        }

        _flyoutActive = false;
    }
}
