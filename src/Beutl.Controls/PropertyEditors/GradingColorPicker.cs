using System.Globalization;
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
using FluentAvalonia.UI.Media;

#nullable enable

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

    private readonly CompositeDisposable _disposables = [];
    private ColorSpectrum? _spectrum;
    private ColorSpectrum? _ringSpectrum;
    private ColorPreviewer? _previewer;
    private ColorSlider? _thirdComponentSlider;
    private ColorSlider? _component1Slider, _component2Slider, _component3Slider;
    private ComboBox? _colorType;
    private ToggleButton? _detailsButton;
    private ToggleButton? _spectrumShapeButton;
    private ColorComponentsEditor? _componentsBox;
    private TextBox? _intensityBox;
    private bool _ignoreColorChange;
    private GradingColor _oldColor;

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

    public void SetColor(GradingColor value)
    {
        GradingColor oldValue = Color;
        ColorChanged?.Invoke(this, (oldValue, value));
        ColorConfirmed?.Invoke(this, (oldValue, value));
    }

    private ColorSlider?[] GetColorSliders() => [
        _thirdComponentSlider,
        _component1Slider,
        _component2Slider,
        _component3Slider];

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        UnregisterEvents();

        base.OnApplyTemplate(e);
        _spectrum = e.NameScope.Find<ColorSpectrum>("Spectrum");
        _ringSpectrum = e.NameScope.Find<ColorSpectrum>("RingSpectrum");
        _previewer = e.NameScope.Find<ColorPreviewer>("Previewer");
        _thirdComponentSlider = e.NameScope.Find<ColorSlider>("ThirdComponentSlider");
        _component1Slider = e.NameScope.Find<ColorSlider>("Component1Slider");
        _component2Slider = e.NameScope.Find<ColorSlider>("Component2Slider");
        _component3Slider = e.NameScope.Find<ColorSlider>("Component3Slider");

        _colorType = e.NameScope.Find<ComboBox>("ColorType");
        _detailsButton = e.NameScope.Find<ToggleButton>("ToggleDetailsButton");
        _spectrumShapeButton = e.NameScope.Find<ToggleButton>("ToggleSpectrumShapeButton");
        _componentsBox = e.NameScope.Find<ColorComponentsEditor>("ColorComponentsBox");
        _intensityBox = e.NameScope.Find<TextBox>("IntensityBox");

        if (_spectrum != null)
        {
            _spectrum.ColorChanged += OnSpectrumColorChanged;
            _spectrum.AddHandler(PointerPressedEvent, OnSpectrumPointerPressed, handledEventsToo: true);
            _spectrum.AddHandler(PointerReleasedEvent, OnSpectrumPointerReleased, handledEventsToo: true);
        }
        if (_ringSpectrum != null)
        {
            _ringSpectrum.ColorChanged += OnSpectrumColorChanged;
            _ringSpectrum.AddHandler(PointerPressedEvent, OnSpectrumPointerPressed, handledEventsToo: true);
            _ringSpectrum.AddHandler(PointerReleasedEvent, OnSpectrumPointerReleased, handledEventsToo: true);
        }
        if (_previewer != null)
        {
            _previewer.ColorChanged += OnSpectrumColorChanged;
            _previewer.AddHandler(PointerPressedEvent, OnSpectrumPointerPressed, handledEventsToo: true);
            _previewer.AddHandler(PointerReleasedEvent, OnSpectrumPointerReleased, handledEventsToo: true);
        }

        foreach (ColorSlider? item in GetColorSliders())
        {
            if (item != null)
            {
                item.ColorChanged += OnColorSliderColorChanged;
                item.AddHandler(PointerPressedEvent, OnSpectrumPointerPressed, handledEventsToo: true);
                item.AddHandler(PointerReleasedEvent, OnSpectrumPointerReleased, handledEventsToo: true);
            }
        }

        if (_componentsBox != null)
        {
            _componentsBox.ValueChanged += OnComponentsBoxValueChanged;
            _componentsBox.ValueConfirmed += OnComponentsBoxValueConfirmed;
        }

        _intensityBox?.GetPropertyChangedObservable(TextBox.TextProperty)
            .Subscribe(OnIntensityBoxTextChanged)
            .DisposeWith(_disposables);

        _intensityBox?.AddDisposableHandler(PointerWheelChangedEvent, OnIntensityBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);

        _colorType?.GetPropertyChangedObservable(SelectingItemsControl.SelectedIndexProperty)
            .Subscribe(_ => OnColorTypeChanged())
            .DisposeWith(_disposables);

        if (_detailsButton != null)
        {
            _detailsButton.GetObservable(ToggleButton.IsCheckedProperty)
                .Subscribe(OnToggleDetailsButtonIsCheckedChanged)
                .DisposeWith(_disposables);
        }

        if (_spectrumShapeButton != null)
        {
            _spectrumShapeButton.GetObservable(ToggleButton.IsCheckedProperty)
                .Subscribe(OnToggleSpectrumShapeButtonIsCheckedChanged)
                .DisposeWith(_disposables);
        }

        UpdateColorFromGradingColor(Color);
        OnInputTypeChanged();
    }

    private void OnComponentsBoxValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (_componentsBox != null)
        {
            float intensity = Color.Intensity;
            GradingColor ToGradingColor((int, int, int) tuple)
            {
                if (_componentsBox.Rgb)
                {
                    return new GradingColor(
                        tuple.Item1 / 255f,
                        tuple.Item2 / 255f,
                        tuple.Item3 / 255f,
                        intensity);
                }
                else
                {
                    var color = Color2.FromHSV(tuple.Item1, tuple.Item2, tuple.Item3);
                    return new GradingColor(
                        color.Rf,
                        color.Gf,
                        color.Bf,
                        intensity);
                }
            }

            if (e is PropertyEditorValueChangedEventArgs<(int, int, int)> ee)
            {
                (int, int, int) oldValue = ee.OldValue;
                (int, int, int) newValue = ee.NewValue;
                ColorConfirmed?.Invoke(this, (ToGradingColor(oldValue), ToGradingColor(newValue)));
            }
        }
    }

    private void OnComponentsBoxValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (_componentsBox != null)
        {
            UpdateColor(_componentsBox.Color, ignoreComponents: true);
        }
    }

    private void OnSpectrumPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _oldColor = Color;
    }

    private void OnSpectrumPointerReleased(object? sender, PointerReleasedEventArgs e)
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
            UpdateColorFromGradingColor(Color);
        }
        else if (change.Property == InputTypeProperty)
        {
            OnInputTypeChanged();
        }
    }

    private void OnToggleDetailsButtonIsCheckedChanged(bool? value)
    {
        PseudoClasses.Set(":details", value == true);

        if (_spectrum != null && _ringSpectrum != null && _previewer != null)
        {
            if (value != true)
            {
                OnToggleSpectrumShapeButtonIsCheckedChanged(_spectrumShapeButton?.IsChecked);
            }
            else
            {
                _spectrum.IsVisible = _ringSpectrum.IsVisible = false;
            }

            _previewer.IsVisible = value == true;
        }
    }

    private void OnToggleSpectrumShapeButtonIsCheckedChanged(bool? value)
    {
        if (_spectrum != null && _ringSpectrum != null && _thirdComponentSlider != null)
        {
            _ringSpectrum.IsVisible = value == true;
            _spectrum.IsVisible = value != true;

            _thirdComponentSlider.ColorComponent = value == true
                ? _ringSpectrum.ThirdComponent
                : _spectrum.ThirdComponent;
        }
    }

    private void OnColorTypeChanged()
    {
        if (_colorType != null)
        {
            InputType = _colorType.SelectedIndex switch
            {
                1 => GradingColorPickerInputType.Hsv,
                0 or _ => GradingColorPickerInputType.Rgb,
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

        if (_colorType != null)
        {
            _colorType.SelectedIndex = index;
        }

        if (_componentsBox != null)
        {
            _componentsBox.Rgb = InputType == GradingColorPickerInputType.Rgb;
        }
    }

    private void OnIntensityBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_intensityBox != null
            && !DataValidationErrors.GetHasErrors(_intensityBox)
            && _intensityBox.IsKeyboardFocusWithin
            && TryParseIntensity(_intensityBox.Text, out float value))
        {
            float delta = 10;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                delta = 1;
            }

            value = e.Delta.Y switch
            {
                < 0 => value - delta,
                > 0 => value + delta,
                _ => value
            };

            float newIntensity = value / 100f;
            UpdateGradingColorFromCurrentState(newIntensity);

            e.Handled = true;
        }
    }

    private static bool TryParseIntensity(string? s, out float r)
    {
        r = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string text = s.Trim() ?? "";
        if (text.EndsWith('%'))
        {
            text = text.TrimEnd('%');
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out r)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out r);
    }

    private void OnIntensityBoxTextChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_intensityBox != null && !_ignoreColorChange)
        {
            if (TryParseIntensity(_intensityBox.Text, out float f))
            {
                DataValidationErrors.ClearErrors(_intensityBox);
                float newIntensity = f / 100f;
                UpdateGradingColorFromCurrentState(newIntensity, ignoreIntensity: true);
            }
            else
            {
                DataValidationErrors.SetErrors(_intensityBox, DataValidationMessages.InvalidString);
            }
        }
    }

    private void OnColorSliderColorChanged(object? sender, ColorChangedEventArgs args)
    {
        if (_ignoreColorChange) return;

        Color2 newColor = args.NewColor;
        if (sender is ColorSlider { ColorModel: ColorModel.Hsva } slider)
        {
            HsvColor hsv = slider.HsvColor;
            Color2 oldColor = GetCurrentColor2();
            newColor = Color2.FromHSVf((float)hsv.H, (float)hsv.S, (float)hsv.V, 1f);

            if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component3 })
            {
                newColor = Color2.FromHSV(oldColor.Hue, oldColor.Saturation, newColor.Value, 255);
            }
            else if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component2 })
            {
                newColor = Color2.FromHSV(oldColor.Hue, newColor.Saturation, oldColor.Value, 255);
            }
            else if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component1 })
            {
                newColor = Color2.FromHSV(newColor.Hue, oldColor.Saturation, oldColor.Value, 255);
            }
        }

        UpdateColor(newColor);
    }

    private void OnSpectrumColorChanged(object? sender, ColorChangedEventArgs args)
    {
        HsvColor color = args.NewColor.ToHsv();
        if (sender is ColorSpectrum spectrum)
        {
            color = spectrum.HsvColor;
        }
        else if (sender is ColorPreviewer previewer)
        {
            color = previewer.HsvColor;
        }

        UpdateColor(Color2.FromHSVf((float)color.H, (float)color.S, (float)color.V, 1f));
    }

    private Color2 GetCurrentColor2()
    {
        var gc = Color;
        // Use normalized RGB values (without intensity applied)
        return Color2.FromRGBf(
            Math.Clamp(gc.R, 0f, 1f),
            Math.Clamp(gc.G, 0f, 1f),
            Math.Clamp(gc.B, 0f, 1f),
            1f);
    }

    private void UpdateColorFromGradingColor(GradingColor gc)
    {
        if (_ignoreColorChange) return;

        // Use R, G, B directly (normalized 0-1), Intensity is stored separately
        var color2 = Color2.FromRGBf(
            Math.Clamp(gc.R, 0f, 1f),
            Math.Clamp(gc.G, 0f, 1f),
            Math.Clamp(gc.B, 0f, 1f),
            1f);

        UpdateColorInternal(color2, updateGradingColor: false, intensity: gc.Intensity);
    }

    private void UpdateGradingColorFromCurrentState(float? newIntensity = null, bool ignoreIntensity = false)
    {
        if (_ignoreColorChange) return;

        try
        {
            _ignoreColorChange = true;

            var color2 = GetCurrentColor2();
            GradingColor oldColor = Color;
            float intensity = newIntensity ?? oldColor.Intensity;
            GradingColor newColor = new(
                color2.Rf,
                color2.Gf,
                color2.Bf,
                intensity);

            Color = newColor;

            if (_intensityBox != null && !ignoreIntensity)
            {
                _intensityBox.Text = (intensity * 100f).ToString("F0") + "%";
            }

            ColorChanged?.Invoke(this, (oldColor, newColor));
        }
        finally
        {
            _ignoreColorChange = false;
        }
    }

    private void UpdateColor(Color2 color, bool ignoreComponents = false)
    {
        UpdateColorInternal(color, ignoreComponents, updateGradingColor: true);
    }

    private void UpdateColorInternal(Color2 color, bool ignoreComponents = false, bool updateGradingColor = true, float? intensity = null)
    {
        if (_ignoreColorChange) return;

        try
        {
            _ignoreColorChange = true;
            GradingColor oldGradingColor = Color;
            float currentIntensity = intensity ?? oldGradingColor.Intensity;

            HsvColor hsv = HsvColor.FromHsv(color.Hue, color.Saturationf, color.Valuef);

            if (_spectrum != null)
                _spectrum.HsvColor = hsv;

            if (_ringSpectrum != null)
                _ringSpectrum.HsvColor = hsv;

            if (_previewer != null)
                _previewer.HsvColor = hsv;

            foreach (ColorSlider? item in GetColorSliders())
            {
                if (item != null)
                {
                    if (item.ColorModel == ColorModel.Hsva)
                    {
                        item.HsvColor = hsv;
                    }
                    else
                    {
                        item.Color = color;
                    }
                }
            }

            if (_componentsBox != null && !ignoreComponents)
                _componentsBox.Color = color;

            if (_intensityBox != null)
                _intensityBox.Text = (currentIntensity * 100f).ToString("F0") + "%";

            if (updateGradingColor)
            {
                GradingColor newGradingColor = new(
                    color.Rf,
                    color.Gf,
                    color.Bf,
                    currentIntensity);

                Color = newGradingColor;
                ColorChanged?.Invoke(this, (oldGradingColor, newGradingColor));
            }
        }
        finally
        {
            _ignoreColorChange = false;
        }
    }

    private void UnregisterEvents()
    {
        _disposables.Clear();

        if (_spectrum != null)
        {
            _spectrum.ColorChanged -= OnSpectrumColorChanged;
            _spectrum.RemoveHandler(PointerPressedEvent, OnSpectrumPointerPressed);
            _spectrum.RemoveHandler(PointerReleasedEvent, OnSpectrumPointerReleased);
        }
        if (_ringSpectrum != null)
        {
            _ringSpectrum.ColorChanged -= OnSpectrumColorChanged;
            _ringSpectrum.RemoveHandler(PointerPressedEvent, OnSpectrumPointerPressed);
            _ringSpectrum.RemoveHandler(PointerReleasedEvent, OnSpectrumPointerReleased);
        }
        if (_previewer != null)
        {
            _previewer.ColorChanged -= OnSpectrumColorChanged;
            _previewer.RemoveHandler(PointerPressedEvent, OnSpectrumPointerPressed);
            _previewer.RemoveHandler(PointerReleasedEvent, OnSpectrumPointerReleased);
        }
        if (_componentsBox != null)
        {
            _componentsBox.ValueChanged -= OnComponentsBoxValueChanged;
            _componentsBox.ValueConfirmed -= OnComponentsBoxValueConfirmed;
        }

        foreach (ColorSlider? item in GetColorSliders())
        {
            if (item != null)
            {
                item.ColorChanged -= OnColorSliderColorChanged;
                item.RemoveHandler(PointerPressedEvent, OnSpectrumPointerPressed);
                item.RemoveHandler(PointerReleasedEvent, OnSpectrumPointerReleased);
            }
        }
    }
}
