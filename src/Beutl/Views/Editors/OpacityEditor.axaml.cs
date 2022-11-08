using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class OpacityEditor : UserControl
{
    private static readonly Binding s_binding = new("Value.Value", BindingMode.OneWay)
    {
        Converter = new FuncValueConverter<float, string>(v => $"{v:p}")
    };
    private bool _pressed;
    private bool _rightPressed;
    private float _oldOpacity;

    public OpacityEditor()
    {
        Resources["OpacityToTransform"] = TranslateXConverter.Instance;
        InitializeComponent();
        area.AddHandler(PointerReleasedEvent, Item_PointerReleased, RoutingStrategies.Tunnel);
        numberbox[!TextBox.TextProperty] = s_binding;
        numberbox.GotFocus += NumberBox_GotFocus;
        numberbox.LostFocus += NumberBox_LostFocus;
        numberbox.AddHandler(PointerWheelChangedEvent, NumberBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        numberbox.GetObservable(TextBox.TextProperty).Subscribe(NumberBox_TextChanged);
    }

    private bool TryParse(out float result)
    {
        string? s = numberbox.Text;
        if (s == null)
        {
            result = default;
            return false;
        }

        bool hasPercent = s.EndsWith('%');
        if (float.TryParse(s.AsSpan().TrimEnd('%'), out result))
        {
            if (hasPercent)
            {
                result /= 100f;
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    private void NumberBox_TextChanged(string? s)
    {
        if (!numberbox.IsKeyboardFocusWithin) return;
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is OpacityEditorViewModel { WrappedProperty: { } property })
            {
                await Task.Delay(10);

                if (TryParse(out float value))
                {
                    property.SetValue(Math.Clamp(value, 0, 1));
                }
            }
        });
    }

    private void NumberBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is OpacityEditorViewModel { WrappedProperty: { } property }
            && numberbox.IsKeyboardFocusWithin
            && TryParse(out float value))
        {
            value = e.Delta.Y switch
            {
                < 0 => value - 0.1f,
                > 0 => value + 0.1f,
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => value - 0.01f,
                > 0 => value + 0.01f,
                _ => value
            };

            property.SetValue(Math.Clamp(value, 0, 1));

            e.Handled = true;
        }
    }

    private void NumberBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OpacityEditorViewModel viewModel
            && TryParse(out float value))
        {
            viewModel.SetValue(_oldOpacity, value);
        }
    }

    private void NumberBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is OpacityEditorViewModel viewModel)
        {
            _oldOpacity = viewModel.Value.Value;
        }
    }

    private void Item_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is OpacityEditorViewModel viewModel && _pressed)
        {
            double width = area.Bounds.Width - 20;
            double x = e.GetCurrentPoint(area).Position.X - 10;

            viewModel.WrappedProperty.SetValue(Math.Clamp((float)(x / width), 0, 1));
        }
    }

    private void Item_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(area);
        if (point.Properties.IsRightButtonPressed)
        {
            _rightPressed = true;
        }
        else if (DataContext is OpacityEditorViewModel viewModel)
        {
            _oldOpacity = viewModel.Value.Value;
            double width = area.Bounds.Width - 20;
            double x = point.Position.X - 10;

            viewModel.WrappedProperty.SetValue(Math.Clamp((float)(x / width), 0, 1));
            _pressed = true;
        }
    }

    private void Item_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rightPressed)
        {
            popup.Open();
            _rightPressed = false;
        }
        else if (DataContext is OpacityEditorViewModel viewModel)
        {
            viewModel.SetValue(_oldOpacity, viewModel.Value.Value);
        }

        _pressed = false;
    }

    private void Item_PointerExited(object? sender, PointerEventArgs e)
    {
        _pressed = false;
    }

    private sealed class TranslateXConverter : IMultiValueConverter
    {
        public static readonly TranslateXConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values[0] is float opacity && values[1] is double width)
            {
                var transformBuilder = new TransformOperations.Builder(1);
                transformBuilder.AppendTranslate((opacity * (width - 20)) + 10, 0);
                return transformBuilder.Build();
            }
            else
            {
                return 0;
            }
        }
    }
}
