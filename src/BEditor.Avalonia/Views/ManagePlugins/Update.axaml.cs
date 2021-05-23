using System.Net;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.ManagePlugins;

namespace BEditor.Views.ManagePlugins
{
    public partial class Update : UserControl
    {
        public Update()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);

            if (e.Parent.LogicalParent is TabControl tab &&
                tab.Items is AvaloniaList<object> items &&
                items[1] is TabItem tabitem &&
                tabitem.Content is Library lib &&
                lib.DataContext is LibraryViewModel lvm &&
                DataContext is UpdateViewModel vm)
            {
                vm.Initialize(lvm);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}