using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Update : UserControl
    {
        public Update()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}