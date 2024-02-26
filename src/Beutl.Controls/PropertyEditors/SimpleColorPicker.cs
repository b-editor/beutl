using System.Reactive.Disposables;

using Avalonia;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using Beutl.Reactive;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;

using ColorChangedEventArgs = FluentAvalonia.UI.Controls.ColorChangedEventArgs;
using ColorSpectrum = FluentAvalonia.UI.Controls.ColorSpectrum;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public enum SimpleColorPickerInputType
{
    Hex,
    Rgb,
    Hsv
}

public class SimpleColorPicker : TemplatedControl
{
    public static readonly StyledProperty<Color2> ColorProperty =
        AvaloniaProperty.Register<SimpleColorPicker, Color2>(nameof(Color),
            Colors.Red, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<SimpleColorPickerInputType> InputTypeProperty =
        AvaloniaProperty.Register<SimpleColorPicker, SimpleColorPickerInputType>(nameof(InputType));

    private readonly CompositeDisposable _disposables = [];
    private ColorSpectrum? _spectrum;
    private ColorRamp? _thirdComponentRamp;
    private ColorRamp? _spectrumAlphaRamp;
    private ComboBox? _colorType;
    private ToggleButton? _dropperButton;
    private ColorComponentsEditor? _componentsBox;
    private TextBox? _hexBox;
    private TextBox? _opacityBox;
    private bool _ignoreColorChange;
    private CancellationTokenSource? _cts;

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();

        base.OnApplyTemplate(e);
        _spectrum = e.NameScope.Find<ColorSpectrum>("Spectrum");
        _thirdComponentRamp = e.NameScope.Find<ColorRamp>("ThirdComponentRamp");
        _spectrumAlphaRamp = e.NameScope.Find<ColorRamp>("SpectrumAlphaRamp");
        _colorType = e.NameScope.Find<ComboBox>("ColorType");
        _dropperButton = e.NameScope.Find<ToggleButton>("ColorDropperButton");
        _componentsBox = e.NameScope.Find<ColorComponentsEditor>("ColorComponentsBox");
        _hexBox = e.NameScope.Find<TextBox>("HexBox");
        _opacityBox = e.NameScope.Find<TextBox>("OpacityBox");

        if (_spectrum != null)
        {
            _spectrum.ColorChanged += OnSpectrumColorChanged;
            _disposables.Add(Disposable.Create(() => _spectrum.ColorChanged -= OnSpectrumColorChanged));
        }

        if (_thirdComponentRamp != null)
        {
            _thirdComponentRamp.ColorChanged += OnThirdComponentRampColorChanged;
            _disposables.Add(Disposable.Create(() => _thirdComponentRamp.ColorChanged -= OnThirdComponentRampColorChanged));
        }

        if (_spectrumAlphaRamp != null)
        {
            _spectrumAlphaRamp.ColorChanged += OnSpectrumAlphaRampColorChanged;
            _disposables.Add(Disposable.Create(() => _spectrumAlphaRamp.ColorChanged -= OnSpectrumAlphaRampColorChanged));
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
        }
    }
#endif

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
        if (_opacityBox!=null
            &&!DataValidationErrors.GetHasErrors(_opacityBox)
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

    private void OnSpectrumAlphaRampColorChanged(ColorPickerComponent sender, ColorChangedEventArgs args)
    {
        if (_spectrumAlphaRamp != null)
        {
            UpdateColor(_spectrumAlphaRamp.Color);
        }
    }

    private void OnThirdComponentRampColorChanged(ColorPickerComponent sender, ColorChangedEventArgs args)
    {
        if (_thirdComponentRamp != null)
        {
            UpdateColor(_thirdComponentRamp.Color);
        }
    }

    private void OnSpectrumColorChanged(ColorPickerComponent sender, ColorChangedEventArgs args)
    {
        UpdateColor(args.NewColor);
    }

    private void UpdateColor(Color2 color, bool ignoreOpacity = false, bool ignoreComponents = false, bool ignoreHex = false)
    {
        if (_ignoreColorChange) return;

        try
        {
            _ignoreColorChange = true;
            Color = color;

            if (_spectrum != null)
                _spectrum.Color = color;

            if (_thirdComponentRamp != null)
                _thirdComponentRamp.Color = color;

            if (_spectrumAlphaRamp != null)
                _spectrumAlphaRamp.Color = color;

            if (_componentsBox != null && !ignoreComponents)
                _componentsBox.Color = color;

            if (_hexBox != null && !ignoreHex)
                _hexBox.Text = color.ToHexString(false);

            if (_opacityBox != null && !ignoreOpacity)
                _opacityBox.Text = color.Af.ToString("P0");
        }
        finally
        {
            _ignoreColorChange = false;
        }
    }
}
