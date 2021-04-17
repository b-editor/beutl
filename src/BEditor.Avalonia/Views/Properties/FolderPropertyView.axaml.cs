using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class FolderPropertyView : UserControl, IDisposable
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