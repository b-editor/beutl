using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// A NumberEditor with a slider, raising the same ValueChanged and ValueConfirmed as a scrub of the header.
// A ValueSlider beside the box follows the pointer in steps of SmallChange: a press on the thumb grabs it where it is,
// one elsewhere puts the value there, Alt or Command make a drag a tenth as fast, and a double click returns to Origin,
// the property's default.
// A ValueSliderField over the box is dragged as the header is, by how far the pointer moves and with the pointer
// locked, wherever in the box the drag starts. A press that does not travel edits the text instead.
[TemplatePart("PART_Slider", typeof(ValueSlider))]
[PseudoClasses(":sliding")]
public class NumberSliderEditor<TValue> : NumberEditor<TValue>
    where TValue : INumber<TValue>
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NumberSliderEditor<TValue>, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NumberSliderEditor<TValue>, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> OriginProperty =
        AvaloniaProperty.Register<NumberSliderEditor<TValue>, double>(nameof(Origin));

    private ValueSlider? _slider;
    private HeaderScrubGesture _scrub;
    private TValue _slideStart = TValue.Zero;
    private double _slideValue;
    private double _grabOffset;
    private double _lastX;
    private bool _sliding;

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    private bool IsPressed => _sliding || _scrub.IsArmed;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_slider != null)
        {
            _slider.PointerPressed -= OnSliderPointerPressed;
            _slider.PointerMoved -= OnSliderPointerMoved;
            _slider.PointerReleased -= OnSliderPointerReleased;
            _slider.PointerCaptureLost -= OnSliderPointerCaptureLost;
            _slider.ContextRequested -= OnSliderContextRequested;
        }

        base.OnApplyTemplate(e);
        _slider = e.NameScope.Find<ValueSlider>("PART_Slider");
        if (_slider != null)
        {
            _slider.PointerPressed += OnSliderPointerPressed;
            _slider.PointerMoved += OnSliderPointerMoved;
            _slider.PointerReleased += OnSliderPointerReleased;
            _slider.PointerCaptureLost += OnSliderPointerCaptureLost;
            _slider.ContextRequested += OnSliderContextRequested;
            UpdateSlider();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty
            || change.Property == MinimumProperty
            || change.Property == MaximumProperty
            || change.Property == OriginProperty)
        {
            UpdateSlider();
        }
    }

    private void UpdateSlider()
    {
        if (_slider == null)
            return;

        _slider.Minimum = Minimum;
        _slider.Maximum = Maximum;
        _slider.Origin = Origin;
        _slider.Value = double.CreateTruncating(Value);
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_slider == null || IsReadOnly)
            return;

        PointerPoint point = e.GetCurrentPoint(_slider);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        if (_slider.EditsOnClick)
        {
            _scrub.Arm(point.Position);
            e.Pointer.Capture(_slider);
            return;
        }

        if (e.ClickCount == 2)
        {
            // The default is applied as it is, even when it does not fall on a step.
            TValue before = Value;
            ApplyValue(TValue.CreateTruncating(Origin));
            ConfirmValue(before);
            return;
        }

        e.Pointer.Capture(_slider);
        GrabThumb(_slider, point.Position.X);
    }

    private void BeginSlide()
    {
        _sliding = true;
        PseudoClasses.Set(":sliding", true);
        _slideStart = Value;
    }

    private void GrabThumb(ValueSlider slider, double x)
    {
        BeginSlide();
        double thumbX = slider.XOf(double.CreateTruncating(Value));
        bool onThumb = Math.Abs(x - thumbX) <= ValueSlider.ThumbRadius + 2;
        _lastX = x;
        _slideValue = double.CreateTruncating(Value);
        _grabOffset = onThumb ? x - thumbX : 0;
        if (!onThumb)
        {
            _slideValue = slider.ValueAt(x);
            Slide(_slideValue);
        }
    }

    private void OnSliderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsPressed || _slider == null)
            return;

        PointerPoint point = e.GetCurrentPoint(_slider);
        if (!point.Properties.IsLeftButtonPressed)
        {
            // The release never reached the slider.
            EndPress();
            return;
        }

        e.Handled = true;
        if (_slider.EditsOnClick)
            ScrubBox(_slider, point.Position, e);
        else
            FollowPointer(_slider, point.Position.X, e.KeyModifiers);
    }

    // Moves the value as a drag of the header does, once the press has travelled.
    private void ScrubBox(ValueSlider slider, Point position, PointerEventArgs e)
    {
        if (_scrub.IsArmed)
        {
            if (!_scrub.TryBegin(slider, position))
                return;

            BeginSlide();
        }

        TValue previous = Value;
        ApplyValue(Scrub(previous, ref _scrub, slider, e));
        OnScrubbed(previous, e.Timestamp);
    }

    private void FollowPointer(ValueSlider slider, double x, KeyModifiers modifiers)
    {
        if (NumberEditorHelper.IsFineModifier(modifiers))
        {
            _slideValue = Math.Clamp(_slideValue + ((x - _lastX) * slider.ValuePerPixel * 0.1), Minimum, Maximum);
            // A plain drag goes on from here rather than jumping back under the pointer.
            _grabOffset = x - slider.XOf(_slideValue);
        }
        else
        {
            _slideValue = slider.ValueAt(x - _grabOffset);
        }

        _lastX = x;
        Slide(_slideValue);
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsPressed || e.InitialPressMouseButton != MouseButton.Left)
            return;

        e.Handled = true;
        bool clicked = _scrub.IsArmed;
        EndPress();
        if (clicked)
            EditText();
    }

    // A slider over the box stands in front of the box's own menu of cut, copy and paste, so it opens that.
    private void OnSliderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_slider is not { EditsOnClick: true } || InnerTextBox is not { ContextFlyout: PopupFlyoutBase flyout } textBox)
            return;

        e.Handled = true;
        EditText();
        flyout.ShowAt(textBox, showAtPointer: true);
    }

    private void EditText()
    {
        if (InnerTextBox is { } textBox && textBox.Focus())
            textBox.SelectAll();
    }

    private void OnSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndPress();

    private void EndPress()
    {
        _scrub.Disarm();
        if (!_sliding)
            return;

        _sliding = false;
        PseudoClasses.Set(":sliding", false);
        // Ended before confirming, so a confirmation that rebuilds the slider cannot end it twice.
        if (_scrub.IsActive)
            _scrub.End();

        ConfirmValue(_slideStart);
    }

    private void Slide(double value)
        => ApplyValue(TValue.CreateTruncating(NumberEditorHelper.RoundToStep(value, double.CreateTruncating(SmallChange))));
}
