using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Core.Data;

namespace BEditor.Views.Properties
{
    public class ClipProperty : UserControl
    {
        public ClipProperty(ClipData clip)
        {
            this.DataContext = clip;
            this.InitializeComponent();
        }
        public ClipProperty()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
