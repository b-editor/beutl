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
    public sealed class DocumentPropertyView : UserControl, IDisposable
    {
        public DocumentPropertyView()
        {
            InitializeComponent();
        }

        public DocumentPropertyView(DocumentProperty property)
        {
            DataContext = new DocumentPropertyViewModel(property);
            InitializeComponent();
            var text = this.FindControl<TextBox>("TextBox");
            text.AddHandler(KeyDownEvent, TextBox_KeyDown, RoutingStrategies.Tunnel);

            text.ContextFlyout = null;
        }

        ~DocumentPropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        public void TextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is DocumentPropertyViewModel vm && sender is TextBox tb)
            {
                vm.TextChanged.Execute(tb.Text);
            }
        }

        public void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is DocumentPropertyViewModel vm && sender is TextBox tb)
            {
                vm.LostFocus.Execute(tb.Text);
            }
        }

        public void TextBox_GotFocus(object sender, GotFocusEventArgs e)
        {
            if (DataContext is DocumentPropertyViewModel vm && sender is TextBox tb)
            {
                vm.GotFocus.Execute(tb.Text);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
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
    }
}