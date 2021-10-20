using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class ValuePropertyView : UserControl, IDisposable
    {
        private readonly ValueProperty _property;
        private readonly NumericUpDown _num;
        private bool _isMouseDown;
        private float _oldvalue;
        private Point _startPoint;
        private int _clickCount;
        private DateTime _time;

#pragma warning disable CS8618
        public ValuePropertyView()
#pragma warning restore CS8618
        {
            InitializeComponent();
        }

        public ValuePropertyView(ValueProperty property)
        {
            _property = property;
            DataContext = new ValuePropertyViewModel(property);
            InitializeComponent();

            _num = this.FindControl<NumericUpDown>("Numeric");
            _num.AddHandler(KeyUpEvent, NumericUpDown_KeyUp, RoutingStrategies.Tunnel);
            _num.AddHandler(KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            _num.AddHandler(PointerMovedEvent, NumericUpDown_PointerMoved, RoutingStrategies.Tunnel);
            _num.AddHandler(PointerReleasedEvent, NumericUpDown_PointerReleased, RoutingStrategies.Tunnel);
            _num.AddHandler(PointerPressedEvent, NumericUpDown_PointerPressed, RoutingStrategies.Tunnel);
            _num.AddHandler(PointerLeaveEvent, NumericUpDown_PointerLeave, RoutingStrategies.Tunnel);
        }

        ~ValuePropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DataContext = null;
            GC.SuppressFinalize(this);
        }

        private void NumericUpDown_PointerLeave(object? sender, PointerEventArgs e)
        {
            _isMouseDown = false;
            _clickCount = 0;
        }

        private void NumericUpDown_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            if (num.IsKeyboardFocusWithin) return;
            _clickCount++;
            _isMouseDown = true;
            _startPoint = e.GetPosition(this);

            _oldvalue = _property.Value;

            if (_clickCount == 1 || (DateTime.UtcNow - _time) > TimeSpan.FromSeconds(1))
            {
                e.Handled = true;
                _time = DateTime.UtcNow;
            }
            else
            {
                _clickCount = 0;
            }
        }

        private void NumericUpDown_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isMouseDown = false;

            var newValue = (float)_num.Value;

            _property.Value = _oldvalue;

            if (newValue != _oldvalue)
                _property.ChangeValue(newValue).Execute();
        }

        private void NumericUpDown_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_num.IsKeyboardFocusWithin && _isMouseDown)
            {
                var point = e.GetPosition(this);
                var move = point - _startPoint;

                _num.Value += move.X;

                _startPoint = point;
                _clickCount = 0;
            }
        }

        public void NumericUpDown_GotFocus(object? sender, GotFocusEventArgs e)
        {
            _oldvalue = _property.Value;
        }

        public void NumericUpDown_LostFocus(object? sender, RoutedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var newValue = num.Value;

            _property.Value = _oldvalue;

            _property.ChangeValue((float)newValue).Execute();
        }

        public async void NumericUpDown_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            _property.Value = _property.Clamp((float)e.NewValue);

            await (AppModel.Current.Project!).PreviewUpdateAsync(_property.GetParent<ClipElement>()!);
        }

        private void NumericUpDown_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift && sender is NumericUpDown numeric)
            {
                numeric.Increment = 1;
            }
        }

        private void NumericUpDown_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift && sender is NumericUpDown numeric)
            {
                numeric.Increment = 10;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}