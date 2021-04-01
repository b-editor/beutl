using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.Settings
{
    public class Fonts : UserControl
    {
        public Fonts()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void AddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            var dir = await dialog.ShowAsync((Window)Parent.Parent.Parent);

            if (Directory.Exists(dir))
            {
                BEditor.Settings.Default.IncludeFontDir.Add(dir);
            }
        }
    }
}
