using System;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.Setup;

using Windows.Api;

namespace BEditor.Views.Setup
{
    public sealed class Common : UserControl
    {
        public Common()
        {
            InitializeComponent();
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            if (OperatingSystem.IsWindows()
                && DataContext is CommonViewModel vm
                && vm.AssociateProjectFile.Value)
            {
                new FileLink(".bedit", "BEditor Project File", "BEditor Project File", "open").Link();
            }

            if (VisualRoot is Window w)
            {
                w.Close();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
