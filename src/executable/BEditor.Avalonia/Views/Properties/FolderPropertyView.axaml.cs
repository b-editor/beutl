using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class FolderPropertyView : UserControl, IDisposable
    {
        public FolderPropertyView()
        {
            InitializeComponent();
        }

        public FolderPropertyView(FolderProperty property)
        {
            DataContext = new FolderPropertyViewModel(property);
            InitializeComponent();
        }

        ~FolderPropertyView()
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