using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;

using Beutl.Helpers;
using Beutl.Utilities;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using AM = Avalonia.Media;
using FAM = FluentAvalonia.UI.Media;

namespace Beutl.Views.Editors;

public sealed partial class GradientStopsEditor : UserControl
{
    private Border? _dragging;
    private double _oldOffset;
    private AM.Color _oldColor;
    private bool _pressed;
    private bool _itemsPressed;

    public GradientStopsEditor()
    {
        Resources["GradientStopToTransform"] = TranslateXConverter.Instance;
        Resources["ColorToBrush"] = new FuncValueConverter<AM.Color, ImmutableSolidColorBrush>(
            x => new ImmutableSolidColorBrush(x));
        Resources["ColorToBorderBrush"] = new FuncValueConverter<AM.Color, ImmutableSolidColorBrush>(
            x => new ImmutableSolidColorBrush(ColorGenerator.GetTextColor(x)));
        InitializeComponent();

        colorPicker.FlyoutConfirmed += ColorPicker_FlyoutConfirmed;
        //this.SubscribeDataContextChange<GradientStopsEditorViewModel>(
        //    obj =>
        //    {
        //        border.Background = new AM.LinearGradientBrush
        //        {
        //            GradientStops = obj.Stops,
        //            StartPoint = new(0.0, 0.5, Avalonia.RelativeUnit.Relative),
        //            EndPoint = new(1.0, 0.5, Avalonia.RelativeUnit.Relative),
        //        };
        //    },
        //    _ => border.Background = null);
        border.AddHandler(PointerPressedEvent, Items_PointerPressed, RoutingStrategies.Tunnel);
        border.AddHandler(PointerReleasedEvent, Items_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GradientStopsEditorViewModel viewModel
            && viewModel.SelectedItem.Value is AM.GradientStop stop)
        {
            viewModel.RemoveItem(stop);
        }
    }

    private void ColorPicker_FlyoutConfirmed(ColorPickerButton sender, ColorButtonColorChangedEventArgs args)
    {
        if (DataContext is GradientStopsEditorViewModel viewModel
            && viewModel.SelectedItem.Value is AM.GradientStop stop
            && args.NewColor.HasValue
            && args.OldColor.HasValue)
        {
            stop.Color = args.NewColor.Value;
            viewModel.SaveChange(stop, args.OldColor.Value, stop.Offset);
        }
    }

    private void Items_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _itemsPressed = true;
    }

    private void Items_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        //https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Base/Animation/Animators/ColorAnimator.cs
        static double OECF_sRGB(double linear)
        {
            return linear <= 0.0031308d ? linear * 12.92d : (double)(Math.Pow(linear, 1.0d / 2.4d) * 1.055d - 0.055d);
        }
        static double EOCF_sRGB(double srgb)
        {
            return srgb <= 0.04045d ? srgb / 12.92d : (double)Math.Pow((srgb + 0.055d) / 1.055d, 2.4d);
        }
        static AM.Color InterpolateCore(double progress, AM.Color oldValue, AM.Color newValue)
        {
            var oldA = oldValue.A / 255d;
            var oldR = oldValue.R / 255d;
            var oldG = oldValue.G / 255d;
            var oldB = oldValue.B / 255d;

            var newA = newValue.A / 255d;
            var newR = newValue.R / 255d;
            var newG = newValue.G / 255d;
            var newB = newValue.B / 255d;

            // convert from sRGB to linear
            oldR = EOCF_sRGB(oldR);
            oldG = EOCF_sRGB(oldG);
            oldB = EOCF_sRGB(oldB);

            newR = EOCF_sRGB(newR);
            newG = EOCF_sRGB(newG);
            newB = EOCF_sRGB(newB);

            // compute the interpolated color in linear space
            var a = oldA + progress * (newA - oldA);
            var r = oldR + progress * (newR - oldR);
            var g = oldG + progress * (newG - oldG);
            var b = oldB + progress * (newB - oldB);

            // convert back to sRGB in the [0..255] range
            a *= 255d;
            r = OECF_sRGB(r) * 255d;
            g = OECF_sRGB(g) * 255d;
            b = OECF_sRGB(b) * 255d;

            return new AM.Color((byte)Math.Round(a), (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
        }
        static AM.Color Interpolate(AM.GradientStop prev, AM.GradientStop next, float offset)
        {
            double progress = (offset - prev.Offset) / next.Offset - prev.Offset;
            return InterpolateCore(progress, prev.Color, next.Color);
        }

        if (_itemsPressed
            && DataContext is GradientStopsEditorViewModel viewModel
            && e.InitialPressMouseButton == MouseButton.Right)
        {
            double width = items.Bounds.Width - 20;
            double x = e.GetCurrentPoint(items).Position.X - 10;
            float offset = (float)(x / width);
            AM.Color? color = null;

            AM.GradientStop? next = null;
            int index = 0;

            for (int i = 0; i < viewModel.Stops.Count; i++)
            {
                var cur = viewModel.Stops[i];
                if (MathUtilities.LessThanOrClose(cur.Offset, offset))
                {
                    color = cur.Color;
                    index = i + 1;
                    if (i < viewModel.Stops.Count - 1)
                    {
                        next = viewModel.Stops[i + 1];
                        if (MathUtilities.LessThanOrClose(offset, next.Offset))
                        {
                            color = Interpolate(cur, next, offset);
                            break;
                        }
                    }
                }
                else
                {
                    color = cur.Color;
                    index = i;
                    break;
                }
            }

            if (!color.HasValue)
            {
                color = next?.Color ?? default;
            }

            viewModel.AddItem(new Media.GradientStop(color.Value.ToMedia(), offset), index);

            _itemsPressed = false;
        }
    }

    private void Item_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is AM.GradientStop stop
            && DataContext is GradientStopsEditorViewModel viewModel
            && _pressed)
        {
            double width = items.Bounds.Width - 20;
            double x = e.GetCurrentPoint(items).Position.X - 10;

            stop.Offset = Math.Clamp((float)(x / width), 0, 1);
            viewModel.PushChange(stop);
        }
    }

    private void Item_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is AM.GradientStop stop
            && DataContext is GradientStopsEditorViewModel viewModel)
        {
            _dragging = border;
            _oldOffset = stop.Offset;
            _oldColor = stop.Color;
            _pressed = true;

            viewModel.SelectedItem.Value = stop;
        }
    }

    private void Item_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging?.DataContext is AM.GradientStop stop && DataContext is GradientStopsEditorViewModel viewModel)
        {
            viewModel.SaveChange(stop, _oldColor, _oldOffset);
        }

        _dragging = null;
        _pressed = false;
    }

    private void Item_PointerExited(object? sender, PointerEventArgs e)
    {
        _dragging = null;
        _pressed = false;
    }

    private sealed class TranslateXConverter : IMultiValueConverter
    {
        public static readonly TranslateXConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values[0] is double offset && values[1] is double width)
            {
                var transformBuilder = new TransformOperations.Builder(1);
                transformBuilder.AppendTranslate((offset * (width - 20)) + 10, 0);
                return transformBuilder.Build();
            }
            else
            {
                return 0;
            }
        }
    }
}
