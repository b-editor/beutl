using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class FilePropertyView : UserControl, IDisposable
    {
        public FilePropertyView()
        {
            InitializeComponent();
        }

        public FilePropertyView(FileProperty property)
        {
            DataContext = new FilePropertyViewModel(property);
            InitializeComponent();
        }

        ~FilePropertyView()
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