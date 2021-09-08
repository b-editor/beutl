using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.ViewModels.ManagePlugins;

using FluentAvalonia.UI.Controls;

namespace BEditor.Views.ManagePlugins
{
    public sealed class Search : UserControl
    {
        public Search()
        {
            InitializeComponent();
            if (DataContext is SearchViewModel vm)
            {
                vm.Navigate.Subscribe(async package =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var frame = this.FindLogicalAncestorOfType<Frame>();
                        var navi = this.FindLogicalAncestorOfType<NavigationView>();
                        frame.Navigate(typeof(PackageView), package);
                    });
                });
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}