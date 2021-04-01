using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Controls
{
    public class ContextButton : UserControl
    {
        public ContextButton()
        {
            InitializeComponent();
        }

        public void Button_Click(object s, RoutedEventArgs e)
        {
            ContextMenu?.Open();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
