using System.Diagnostics;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Beutl.Media;
using Beutl.Reactive;
using FluentAvalonia.Core;
using UnboundedHsv = (float H, float S, float V);

namespace Beutl.Controls.PropertyEditors;

public enum GradingColorPickerInputType
{
    Rgb,
    Hsv
}

[PseudoClasses(":details")]
public class GradingColorPicker : TemplatedControl
{
    public static readonly StyledProperty<GradingColor> ColorProperty =
        AvaloniaProperty.Register<GradingColorPicker, GradingColor>(nameof(Color),
            new GradingColor(1, 1, 1), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<GradingColorPickerInputType> InputTypeProperty =
        AvaloniaProperty.Register<GradingColorPicker, GradingColorPickerInputType>(nameof(InputType));

    public static readonly StyledProperty<bool> ShowDetailsProperty =
        AvaloniaProperty.Register<GradingColorPicker, bool>(nameof(ShowDetails));

    private readonly CompositeDisposable _disposables = [];
    private ColorSpectrum? _ringSpectrum;
    private ColorPreviewer? _previewer;
    private GradingWheel? _firstComponentWheel, _secondComponentWheel, _thirdComponentWheel, _fourthComponentWheel;
    private ColorSlider? _hueSlider, _saturationSlider;
    private ComboBox? _colorType;
    private ToggleButton? _detailsButton;
    private GradingColorComponentsEditor? _componentsBox;
    private bool _ignoreColorChange;
    private UnboundedHsv _oldHsv;
    private GradingColor _oldColor;
    private UnboundedHsv _currentHsv;
    private readonly ColorPartPress _press;
    private bool _wheelDragging;
    private bool _wheelStopped;

    public GradingColorPicker()
    {
        _press = new ColorPartPress(this, () => _oldColor = Color, ConfirmPress);
    }

    public event TypedEventHandler<GradingColorPicker, (GradingColor OldValue, GradingColor NewValue)>? ColorChanged;

    public event TypedEventHandler<GradingColorPicker, (GradingColor OldValue, GradingColor NewValue)>? ColorConfirmed;

    public GradingColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public GradingColorPickerInputType InputType
    {
        get => GetValue(InputTypeProperty);
        set => SetValue(InputTypeProperty, value);
    }

    public bool ShowDetails
    {
        get => GetValue(ShowDetailsProperty);
        set => SetValue(ShowDetailsProperty, value);
    }

    // Whether a wheel, spectrum, slider or previewer is being dragged; a stopped drag no longer counts.
    public bool IsDragging => (_wheelDragging && !_wheelStopped) || _press.IsPressed;

    // Ends the drag in progress without ColorConfirmed, for a host that ended the gesture itself.
    public void StopDrag()
    {
        _press.Stop();
        _wheelStopped = _wheelDragging;
    }

    private ColorSlider?[] GetColorSliders() =>
    [
        _hueSlider,
        _saturationSlider
    ];

    private GradingWheel?[] GetWheels() =>
    [
        _firstComponentWheel,
        _secondComponentWheel,
        _thirdComponentWheel,
        _fourthComponentWheel
    ];

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        UnregisterEvents();

        base.OnApplyTemplate(e);
        FindTemplateParts(e.NameScope);
        RegisterEvents();

        UpdateColor((Color, null));
        OnInputTypeChanged();
    }

    private void FindTemplateParts(INameScope nameScope)
    {
        _ringSpectrum = nameScope.Find<ColorSpectrum>("RingSpectrum");
        _previewer = nameScope.Find<ColorPreviewer>("Previewer");
        _firstComponentWheel = nameScope.Find<GradingWheel>("FirstComponentWheel");
        _secondComponentWheel = nameScope.Find<GradingWheel>("SecondComponentWheel");
        _thirdComponentWheel = nameScope.Find<GradingWheel>("ThirdComponentWheel");
        _fourthComponentWheel = nameScope.Find<GradingWheel>("FourthComponentWheel");
        _hueSlider = nameScope.Find<ColorSlider>("HueSlider");
        _saturationSlider = nameScope.Find<ColorSlider>("SaturationSlider");

        _colorType = nameScope.Find<ComboBox>("ColorType");
        _detailsButton = nameScope.Find<ToggleButton>("ToggleDetailsButton");
        _componentsBox = nameScope.Find<GradingColorComponentsEditor>("ColorComponentsBox");
    }

    private void RegisterEvents()
    {
        if (_ringSpectrum != null)
        {
            _ringSpectrum.ColorChanged += OnSpectrumColorChanged;
            _press.Add(_ringSpectrum);
        }

        if (_previewer != null)
        {
            _previewer.ColorChanged += OnSpectrumColorChanged;
            _press.Add(_previewer);
        }

        foreach (ColorSlider? item in GetColorSliders())
        {
            if (item == null) continue;
            item.ColorChanged += OnColorSliderColorChanged;
            _press.Add(item);
        }

        foreach (GradingWheel? item in GetWheels())
        {
            if (item == null) continue;
            item.DragStarted += OnWheelDragStarted;
            item.DragDelta += OnWheelDragDelta;
            item.DragCompleted += OnWheelDragCompleted;
        }

        if (_componentsBox != null)
        {
            _componentsBox.ValueChanged += OnComponentsBoxValueChanged;
            _componentsBox.ValueConfirmed += OnComponentsBoxValueConfirmed;
        }

        _colorType?.GetPropertyChangedObservable(SelectingItemsControl.SelectedIndexProperty)
            .Subscribe(_ => OnColorTypeChanged())
            .DisposeWith(_disposables);

        if (_detailsButton != null)
        {
            _detailsButton.GetObservable(ToggleButton.IsCheckedProperty)
                .Subscribe(OnToggleDetailsButtonIsCheckedChanged)
                .DisposeWith(_disposables);
        }
    }

    private void OnWheelDragStarted(object? sender, VectorEventArgs e)
    {
        _wheelDragging = true;
        _wheelStopped = false;
        _oldHsv = _currentHsv;
        _oldColor = Color;
    }

    private void OnWheelDragDelta(object? sender, VectorEventArgs e)
    {
        if (_ignoreColorChange || _wheelStopped) return;

        if (sender is GradingWheel wheel)
        {
            float delta = (float)(e.Vector.X / wheel.Bounds.Width);
            if ((InputType == GradingColorPickerInputType.Hsv || !PseudoClasses.Contains(":details")) && wheel.Name == "ThirdComponentWheel")
            {
                // Hue, SaturationはSliderで操作するため、Valueのみ変更する
                var hsv = _oldHsv;
                hsv.V += delta;
                UpdateColor((null, hsv));
            }
            else if (InputType == GradingColorPickerInputType.Rgb)
            {
                bool isMaster = wheel.Name == "FourthComponentWheel";
                var color = _oldColor;
                if (isMaster || wheel.Name == "FirstComponentWheel")
                {
                    color = new(color.R + delta, color.G, color.B);
                }
                if (isMaster || wheel.Name == "SecondComponentWheel")
                {
                    color = new(color.R, color.G + delta, color.B);
                }
                if (isMaster || wheel.Name == "ThirdComponentWheel")
                {
                    color = new(color.R, color.G, color.B + delta);
                }

                UpdateColor((color, null));
            }
        }
    }

    private void OnWheelDragCompleted(object? sender, VectorEventArgs e)
    {
        _wheelDragging = false;
        if (_wheelStopped) return;

        var current = Color;
        if (!_oldColor.Equals(current))
        {
            ColorConfirmed?.Invoke(this, (_oldColor, current));
        }
    }

    private void OnComponentsBoxValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (_componentsBox != null && e is PropertyEditorValueChangedEventArgs<(float, float, float)> ee)
        {
            var oldValue = _componentsBox.ToGradingColorFromTuple(ee.OldValue);
            var newValue = _componentsBox.ToGradingColorFromTuple(ee.NewValue);
            ColorConfirmed?.Invoke(this, (oldValue, newValue));
        }
    }

    private void OnComponentsBoxValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (_componentsBox != null && !_ignoreColorChange && e is PropertyEditorValueChangedEventArgs<(float, float, float)> ee)
        {
            var newValue = _componentsBox.GetGradingColorOrUnboundedHsv(ee.NewValue);
            UpdateColor(newValue, ignoreComponents: true);
        }
    }

    private void ConfirmPress()
    {
        GradingColor color = Color;
        GradingColor oldColor = _oldColor;
        if (oldColor != color)
        {
            ColorConfirmed?.Invoke(this, (oldColor, color));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
        {
            UpdateColor((Color, null));
        }
        else if (change.Property == InputTypeProperty)
        {
            OnInputTypeChanged();
        }
        else if (change.Property == ShowDetailsProperty)
        {
            OnShowDetailsChanged(change.GetNewValue<bool>());
        }
    }

    private void OnToggleDetailsButtonIsCheckedChanged(bool? value)
    {
        ShowDetails = value == true;
    }

    private void OnShowDetailsChanged(bool value)
    {
        PseudoClasses.Set(":details", value);
        _detailsButton?.IsChecked = value;
    }

    private void OnColorTypeChanged()
    {
        if (_colorType != null)
        {
            InputType = _colorType.SelectedIndex switch
            {
                1 => GradingColorPickerInputType.Hsv,
                _ => GradingColorPickerInputType.Rgb,
            };
        }
    }

    private void OnInputTypeChanged()
    {
        int index = InputType switch
        {
            GradingColorPickerInputType.Rgb => 0,
            GradingColorPickerInputType.Hsv => 1,
            _ => 0,
        };

        _colorType?.SelectedIndex = index;
        _componentsBox?.Rgb = InputType == GradingColorPickerInputType.Rgb;
    }

    private void OnColorSliderColorChanged(object? sender, ColorChangedEventArgs args)
    {
        if (_ignoreColorChange) return;

        if (sender is ColorSlider { ColorModel: ColorModel.Hsva } slider)
        {
            UnboundedHsv hsv = _currentHsv;

            if (slider is { ColorComponent: ColorComponent.Component2 })
            {
                hsv.S = (float)slider.HsvColor.S;
            }
            else if (slider is { ColorComponent: ColorComponent.Component1 })
            {
                hsv.H = (float)slider.HsvColor.H;
            }

            UpdateColor((null, hsv));
        }
    }

    private void OnSpectrumColorChanged(object? sender, ColorChangedEventArgs args)
    {
        HsvColor color = ColorPickerParts.GetHsvColor(sender, args);

        var hsv = ((float)color.H, (float)color.S, (float)color.V);
        UpdateColor((null, hsv));
    }

    private void UpdateColor((GradingColor?, UnboundedHsv?) color, bool ignoreComponents = false)
    {
        if (_ignoreColorChange) return;

        try
        {
            _ignoreColorChange = true;
            GradingColor oldColor = Color;
            GradingColor newColor = color.Item1 ?? GradingColorHelper.GetColor(color.Item2!.Value);
            UnboundedHsv newHsv = color.Item2 ?? GradingColorHelper.GetUnboundedHsv(color.Item1!.Value);
            _currentHsv = newHsv;
            Color = newColor;

            HsvColor hsv = HsvColor.FromHsv(newHsv.H, newHsv.S, newHsv.V);

            _ringSpectrum?.HsvColor = hsv;
            _previewer?.HsvColor = hsv;

            foreach (ColorSlider? item in GetColorSliders())
            {
                if (item == null) continue;

                if (item.ColorModel == ColorModel.Hsva)
                {
                    item.HsvColor = hsv;
                }
            }

            if (_componentsBox != null && !ignoreComponents)
                _componentsBox.Color = newColor;

            ColorChanged?.Invoke(this, (oldColor, newColor));
        }
        finally
        {
            _ignoreColorChange = false;
        }
    }

    private void UnregisterEvents()
    {
        _disposables.Clear();

        if (_ringSpectrum != null)
        {
            _ringSpectrum.ColorChanged -= OnSpectrumColorChanged;
            _press.Remove(_ringSpectrum);
        }

        if (_previewer != null)
        {
            _previewer.ColorChanged -= OnSpectrumColorChanged;
            _press.Remove(_previewer);
        }

        if (_componentsBox != null)
        {
            _componentsBox.ValueChanged -= OnComponentsBoxValueChanged;
            _componentsBox.ValueConfirmed -= OnComponentsBoxValueConfirmed;
        }

        foreach (ColorSlider? item in GetColorSliders())
        {
            if (item == null) continue;

            item.ColorChanged -= OnColorSliderColorChanged;
            _press.Remove(item);
        }

        foreach (GradingWheel? item in GetWheels())
        {
            if (item == null) continue;

            item.DragStarted -= OnWheelDragStarted;
            item.DragDelta -= OnWheelDragDelta;
            item.DragCompleted -= OnWheelDragCompleted;
        }
    }
}
