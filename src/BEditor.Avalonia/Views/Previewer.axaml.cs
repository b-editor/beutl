using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views
{
    public class Previewer : UserControl
    {
        public Previewer()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}