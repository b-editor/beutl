using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views.Properties
{
    public class ClipPropertyView : UserControl
    {
        public ClipPropertyView()
        {
            InitializeComponent();
        }
        
        public ClipPropertyView(ClipElement clip)
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
