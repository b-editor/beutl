using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class ColorPropertyView : UserControl, IDisposable
    {
        public ColorPropertyView()
        {
            InitializeComponent();
        }

        public ColorPropertyView(ColorProperty property)
        {
            DataContext = new ColorPropertyViewModel(property);
            InitializeComponent();
        }

        ~ColorPropertyView()
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}