using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Core.Data;

namespace BEditor.Views.Properties
{
    public class PropertyTab : UserControl
    {
        private readonly TabControl TabControl;

        public PropertyTab(Scene scene)
        {
            DataContext = scene;
            this.InitializeComponent();
            TabControl = this.FindControl<TabControl>("TabControl");
        }

        public PropertyTab()
        {
            this.InitializeComponent();
            TabControl = this.FindControl<TabControl>("TabControl");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
