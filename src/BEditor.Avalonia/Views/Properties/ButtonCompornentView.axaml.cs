using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class ButtonCompornentView : UserControl, IDisposable
    {
        public ButtonCompornentView()
        {
            InitializeComponent();
        }

        public ButtonCompornentView(ButtonComponent property)
        {
            DataContext = new ButtonComponentViewModel(property);
            InitializeComponent();
        }

        ~ButtonCompornentView()
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