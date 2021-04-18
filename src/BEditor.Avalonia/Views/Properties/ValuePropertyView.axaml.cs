using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class ValuePropertyView : UserControl, IDisposable
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
        }

        ~ValuePropertyView()
        {
            Dispose();
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

        public void NumericUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            var num = (NumericUpDown)sender!;

            _property.Value = _property.Clamp((float)e.NewValue);

            AppModel.Current.Project!.PreviewUpdate(_property.GetParent2()!);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}