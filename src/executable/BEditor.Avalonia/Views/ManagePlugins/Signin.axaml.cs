using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Signin : UserControl
    {
        public Signin()
        {
            InitializeComponent();
        }

        public void Signup(object s, RoutedEventArgs e)
        {
            if (Parent is TabItem item && item.Parent is TabControl tab)
            {
                item.Content = new Signup();

                // VisualTreeÇçXêV
                tab.SelectedItem = null;
                tab.SelectedItem = item;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
