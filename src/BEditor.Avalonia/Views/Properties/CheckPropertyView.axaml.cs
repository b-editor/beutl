using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class CheckPropertyView : UserControl, IDisposable
    {
        public CheckPropertyView()
        {
            InitializeComponent();
        }

        public CheckPropertyView(CheckProperty property)
        {
            DataContext = new CheckPropertyViewModel(property);
            InitializeComponent();
        }

        ~CheckPropertyView()
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}