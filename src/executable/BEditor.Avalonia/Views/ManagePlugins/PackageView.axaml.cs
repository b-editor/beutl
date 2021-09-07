using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public sealed class PackageView : UserControl
    {
        public PackageView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
