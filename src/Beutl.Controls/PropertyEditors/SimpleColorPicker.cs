using System.Reactive.Disposables;

using Avalonia;

using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using Beutl.Reactive;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Media;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public enum SimpleColorPickerInputType
{
    Hex,
    Rgb,
    Hsv
}

[PseudoClasses(":details")]
public class SimpleColorPicker : TemplatedControl
{
    public static readonly StyledProperty<Color2> ColorProperty =
        AvaloniaProperty.Register<SimpleColorPicker, Color2>(nameof(Color),
            Colors.Red, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<SimpleColorPickerInputType> InputTypeProperty =
        AvaloniaProperty.Register<SimpleColorPicker, SimpleColorPickerInputType>(nameof(InputType));

    private readonly CompositeDisposable _disposables = [];
    private ColorSpectrum? _spectrum;
    private ColorSpectrum? _ringSpectrum;
    private ColorPreviewer? _previewer;
    private ColorSlider? _thirdComponentSlider;
    private ColorSlider? _spectrumAlphaSlider;
    private ColorSlider? _component1Slider, _component2Slider, _component3Slider;
    private ComboBox? _colorType;
    private ToggleButton? _dropperButton;
    private ToggleButton? _detailsButton;
    private ToggleButton? _spectrumShapeButton;
    private ColorComponentsEditor? _componentsBox;
    private TextBox? _hexBox;
    private TextBox? _opacityBox;
    private bool _ignoreColorChange;
#if WINDOWS
    private CancellationTokenSource? _cts;
#endif

    public event TypedEventHandler<SimpleColorPicker, (Color2 OldValue, Color2 NewValue)>? ColorChanged;

    public Color2 Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public SimpleColorPickerInputType InputType
    {
        get => GetValue(InputTypeProperty);
        set => SetValue(InputTypeProperty, value);
    }

    private ColorSlider?[] GetColorSliders() => [
        _thirdComponentSlider, _spectrumAlphaSlider,
        _component1Slider, _component2Slider, _component3Slider];

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();

        base.OnApplyTemplate(e);
        _spectrum = e.NameScope.Find<ColorSpectrum>("Spectrum");
        _ringSpectrum = e.NameScope.Find<ColorSpectrum>("RingSpectrum");
        _previewer = e.NameScope.Find<ColorPreviewer>("Previewer");
        _thirdComponentSlider = e.NameScope.Find<ColorSlider>("ThirdComponentSlider");
        _spectrumAlphaSlider = e.NameScope.Find<ColorSlider>("SpectrumAlphaSlider");
        _component1Slider = e.NameScope.Find<ColorSlider>("Component1Slider");
        _component2Slider = e.NameScope.Find<ColorSlider>("Component2Slider");
        _component3Slider = e.NameScope.Find<ColorSlider>("Component3Slider");

        _colorType = e.NameScope.Find<ComboBox>("ColorType");
        _dropperButton = e.NameScope.Find<ToggleButton>("ColorDropperButton");
        _detailsButton = e.NameScope.Find<ToggleButton>("ToggleDetailsButton");
        _spectrumShapeButton = e.NameScope.Find<ToggleButton>("ToggleSpectrumShapeButton");
        _componentsBox = e.NameScope.Find<ColorComponentsEditor>("ColorComponentsBox");
        _hexBox = e.NameScope.Find<TextBox>("HexBox");
        _opacityBox = e.NameScope.Find<TextBox>("OpacityBox");

        if (_spectrum != null)
        {
            _spectrum.ColorChanged += OnSpectrumColorChanged;
            _disposables.Add(Disposable.Create(() => _spectrum.ColorChanged -= OnSpectrumColorChanged));
        }
        if (_ringSpectrum != null)
        {
            _ringSpectrum.ColorChanged += OnSpectrumColorChanged;
            _disposables.Add(Disposable.Create(() => _ringSpectrum.ColorChanged -= OnSpectrumColorChanged));
        }
        if (_previewer != null)
        {
            _previewer.ColorChanged += OnSpectrumColorChanged;
            _disposables.Add(Disposable.Create(() => _previewer.ColorChanged -= OnSpectrumColorChanged));
        }

        foreach (ColorSlider? item in GetColorSliders())
        {
            if (item != null)
            {
                item.ColorChanged += OnColorSliderColorChanged;
                _disposables.Add(Disposable.Create(item, item => item.ColorChanged -= OnColorSliderColorChanged));
            }
        }

        _componentsBox?.GetPropertyChangedObservable(ColorComponentsEditor.ColorProperty)
            .Subscribe(OnColorComponentsBoxChanged)
            .DisposeWith(_disposables);

        _hexBox?.GetPropertyChangedObservable(TextBox.TextProperty)
            .Subscribe(OnHexBoxTextChanged)
            .DisposeWith(_disposables);

        _opacityBox?.GetPropertyChangedObservable(TextBox.TextProperty)
            .Subscribe(OnOpacityBoxTextChanged)
            .DisposeWith(_disposables);

        _opacityBox?.AddDisposableHandler(PointerWheelChangedEvent, OnOpacityBoxPointerWheelChanged, RoutingStrategies.Tunnel)
            .DisposeWith(_disposables);

        _colorType?.GetPropertyChangedObservable(SelectingItemsControl.SelectedIndexProperty)
            .Subscribe(_ => OnColorTypeChanged())
            .DisposeWith(_disposables);

#if WINDOWS
        if (_dropperButton != null)
        {
            _dropperButton.AddDisposableHandler(Button.ClickEvent, OnDropperButtonClick)
                .DisposeWith(_disposables);

            _dropperButton.IsVisible = true;
        }
#endif
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

        UpdateColor(Color);
        OnInputTypeChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
        {
            UpdateColor(Color);
        }
        else if (change.Property == InputTypeProperty)
        {
            OnInputTypeChanged();
        }
    }

#if WINDOWS
    private async void OnDropperButtonClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        if (_dropperButton != null && _dropperButton.IsChecked == true)
        {
            var toplevel = TopLevel.GetTopLevel(this) as Window;
            if (toplevel != null)
                toplevel.Topmost = true;

            try
            {
                (Color2 color, int x, int y) = await ColorDropper.Run(_cts.Token);
                Point clientPoint = _dropperButton.PointToClient(new(x, y));
                if (!new Rect(_dropperButton.Bounds.Size).Contains(clientPoint))
                {
                    UpdateColor(color);
                    _dropperButton.IsChecked = false;
                }
            }
            catch
            {
                _dropperButton.IsChecked = false;
            }
            finally
            {
                if (toplevel != null)
                    toplevel.Topmost = false;
            }
        }
    }
#endif

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
                1 => SimpleColorPickerInputType.Rgb,
                2 => SimpleColorPickerInputType.Hsv,
                0 or _ => SimpleColorPickerInputType.Hex,
            };
        }
    }

    private void OnInputTypeChanged()
    {
        int index = InputType switch
        {
            SimpleColorPickerInputType.Hex => 0,
            SimpleColorPickerInputType.Rgb => 1,
            SimpleColorPickerInputType.Hsv => 2,
            _ => 0,
        };

        if (_colorType != null)
        {
            _colorType.SelectedIndex = index;
        }

        if (index is 1 or 2)
        {
            if (_componentsBox != null)
            {
                _componentsBox.IsVisible = true;
                _componentsBox.Rgb = InputType == SimpleColorPickerInputType.Rgb;
            }

            if (_hexBox != null)
            {
                _hexBox.IsVisible = false;
            }
        }
        else
        {
            if (_componentsBox != null)
            {
                _componentsBox.IsVisible = false;
            }

            if (_hexBox != null)
            {
                _hexBox.IsVisible = true;
            }
        }
    }

    private void OnOpacityBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_opacityBox != null
            && !DataValidationErrors.GetHasErrors(_opacityBox)
            && _opacityBox.IsKeyboardFocusWithin
            && TryParseOpacity(_opacityBox.Text, out float value))
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

            UpdateColor(Color.WithAlphaf(value / 100f));

            e.Handled = true;
        }
    }

    private static bool TryParseOpacity(string? s, out float r)
    {
        r = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string text = s.Trim() ?? "";
        if (text.EndsWith('%'))
        {
            text = text.TrimEnd('%');
        }

        return float.TryParse(text, out r);
    }

    private void OnOpacityBoxTextChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_opacityBox != null && !_ignoreColorChange)
        {
            if (TryParseOpacity(_opacityBox.Text, out float f))
            {
                UpdateColor(Color.WithAlphaf(f / 100f), ignoreOpacity: f >= 0 && f <= 100);
            }
            else
            {
                DataValidationErrors.SetErrors(_opacityBox, DataValidationMessages.InvalidString);
            }
        }
    }

    private void OnHexBoxTextChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_hexBox != null)
        {
            if (Color2.TryParse(_hexBox.Text, out Color2 color))
            {
                UpdateColor(color, ignoreHex: true);
            }
            else
            {
                DataValidationErrors.SetErrors(_hexBox, DataValidationMessages.InvalidString);
            }
        }
    }

    private void OnColorComponentsBoxChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_componentsBox != null)
        {
            UpdateColor(_componentsBox.Color, ignoreComponents: true);
        }
    }

    private void OnColorSliderColorChanged(object? sender, ColorChangedEventArgs args)
    {
        if (_ignoreColorChange) return;

        Color2 newColor = args.NewColor;
        if (sender is ColorSlider { ColorModel: ColorModel.Hsva } slider)
        {
            HsvColor hsv = slider.HsvColor;
            Color2 oldColor = Color;
            newColor = Color2.FromHSVf((float)hsv.H, (float)hsv.S, (float)hsv.V);

            if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component3 })
            {
                newColor = Color2.FromHSV(oldColor.Hue, oldColor.Saturation, newColor.Value);
            }
            else if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component2 })
            {
                newColor = Color2.FromHSV(oldColor.Hue, newColor.Saturation, oldColor.Value);
            }
            else if (slider is { ColorComponent: Avalonia.Controls.ColorComponent.Component1 })
            {
                newColor = Color2.FromHSV(newColor.Hue, oldColor.Saturation, oldColor.Value);
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

        UpdateColor(Color2.FromHSVf((float)color.H, (float)color.S, (float)color.V));
    }

    private void UpdateColor(Color2 color, bool ignoreOpacity = false, bool ignoreComponents = false, bool ignoreHex = false)
    {
        if (_ignoreColorChange) return;

        try
        {
            _ignoreColorChange = true;
            Color2 oldColor = Color;
            Color = color;
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

            if (_hexBox != null && !ignoreHex)
                _hexBox.Text = color.ToHexString(false);

            if (_opacityBox != null && !ignoreOpacity)
                _opacityBox.Text = color.Af.ToString("P0");

            ColorChanged?.Invoke(this, (oldColor, color));
        }
        finally
        {
            _ignoreColorChange = false;
        }
    }
}
