using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// A NumberEditor with a slider beside its box, raising the same ValueChanged and ValueConfirmed as a scrub of the
// header. A press on the rail puts the value there, one on the thumb grabs it where it is, and a drag follows the
// pointer, a tenth as fast with Alt or Command. A double click returns to Origin, the property's default. The value
// moves in steps of SmallChange.
[TemplatePart("PART_Slider", typeof(ValueSlider))]
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_slider != null)
        {
            _slider.PointerPressed -= OnSliderPointerPressed;
            _slider.PointerMoved -= OnSliderPointerMoved;
            _slider.PointerReleased -= OnSliderPointerReleased;
            _slider.PointerCaptureLost -= OnSliderPointerCaptureLost;
        }

        base.OnApplyTemplate(e);
        _slider = e.NameScope.Find<ValueSlider>("PART_Slider");
        if (_slider != null)
        {
            _slider.PointerPressed += OnSliderPointerPressed;
            _slider.PointerMoved += OnSliderPointerMoved;
            _slider.PointerReleased += OnSliderPointerReleased;
            _slider.PointerCaptureLost += OnSliderPointerCaptureLost;
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
        if (e.ClickCount == 2)
        {
            // The default is applied as it is, even when it does not fall on a step.
            TValue before = Value;
            ApplyValue(TValue.CreateTruncating(Origin));
            ConfirmValue(before);
            return;
        }

        double x = point.Position.X;
        double thumbX = _slider.XOf(double.CreateTruncating(Value));
        bool onThumb = Math.Abs(x - thumbX) <= ValueSlider.ThumbRadius + 2;
        _sliding = true;
        _slideStart = Value;
        _slideValue = double.CreateTruncating(Value);
        _grabOffset = onThumb ? x - thumbX : 0;
        _lastX = x;
        e.Pointer.Capture(_slider);
        if (!onThumb)
        {
            _slideValue = _slider.ValueAt(x);
            Slide(_slideValue);
        }
    }

    private void OnSliderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_sliding || _slider == null)
            return;

        PointerPoint point = e.GetCurrentPoint(_slider);
        if (!point.Properties.IsLeftButtonPressed)
        {
            // The release never reached the slider.
            EndSlide();
            return;
        }

        e.Handled = true;
        double x = point.Position.X;
        if (NumberEditorHelper.IsFineModifier(e.KeyModifiers))
        {
            _slideValue = Math.Clamp(_slideValue + ((x - _lastX) * _slider.ValuePerPixel * 0.1), Minimum, Maximum);
            // A plain drag goes on from here rather than jumping back under the pointer.
            _grabOffset = x - _slider.XOf(_slideValue);
        }
        else
        {
            _slideValue = _slider.ValueAt(x - _grabOffset);
        }

        _lastX = x;
        Slide(_slideValue);
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_sliding && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;
            EndSlide();
        }
    }

    private void OnSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndSlide();

    private void EndSlide()
    {
        if (!_sliding)
            return;

        _sliding = false;
        ConfirmValue(_slideStart);
    }

    private void Slide(double value)
        => ApplyValue(TValue.CreateTruncating(NumberEditorHelper.RoundToStep(value, double.CreateTruncating(SmallChange))));
}
