using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class SelectorPropertyView : UserControl, IDisposable
    {
        public SelectorPropertyView()
        {
            InitializeComponent();
        }

        public SelectorPropertyView(ISelectorPropertyViewModel viewmodel)
        {
            DataContext = viewmodel;
            InitializeComponent();
        }

        ~SelectorPropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        public void ComboBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (DataContext is ISelectorPropertyViewModel vm)
            {
                vm.Command.Execute(((ComboBox)s).SelectedIndex);
            }
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}