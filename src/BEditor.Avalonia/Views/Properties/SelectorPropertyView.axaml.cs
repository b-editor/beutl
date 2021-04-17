using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class SelectorPropertyView : UserControl, IDisposable
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
            Dispose();
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