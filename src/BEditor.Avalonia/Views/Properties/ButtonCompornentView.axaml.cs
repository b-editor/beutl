using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class ButtonCompornentView : UserControl
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}