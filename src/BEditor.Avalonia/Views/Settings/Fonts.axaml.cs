using System.IO;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.Settings
{
    public sealed class Fonts : UserControl
    {
        public Fonts()
        {
            InitializeComponent();
        }

        public async void AddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            var dir = await dialog.ShowAsync((Window)VisualRoot);

            if (Directory.Exists(dir))
            {
                BEditor.Settings.Default.IncludeFontDir.Add(dir);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}