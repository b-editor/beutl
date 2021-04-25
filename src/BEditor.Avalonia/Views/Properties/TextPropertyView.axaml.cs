using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class TextPropertyView : UserControl, IDisposable
    {
        public TextPropertyView()
        {
            InitializeComponent();
        }

        public TextPropertyView(TextProperty property)
        {
            DataContext = new TextPropertyViewModel(property);
            InitializeComponent();
            this.FindControl<TextBox>("TextBox").AddHandler(KeyDownEvent, TextBox_KeyDown, RoutingStrategies.Tunnel);
        }

        ~TextPropertyView()
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

        public void TextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is TextPropertyViewModel vm && sender is TextBox tb)
            {
                vm.TextChanged.Execute(tb.Text);
            }
        }

        public void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is TextPropertyViewModel vm && sender is TextBox tb)
            {
                vm.LostFocus.Execute(tb.Text);
            }
        }

        public void TextBox_GotFocus(object sender, GotFocusEventArgs e)
        {
            if (DataContext is TextPropertyViewModel vm && sender is TextBox tb)
            {
                vm.GotFocus.Execute(tb.Text);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}