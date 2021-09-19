using System;

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
        private float _oldvalue;

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

            var num = this.FindControl<NumericUpDown>("Numeric");
            num.AddHandler(KeyUpEvent, NumericUpDown_KeyUp, RoutingStrategies.Tunnel);
            num.AddHandler(KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
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