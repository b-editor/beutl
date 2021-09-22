using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.ViewModels;

namespace BEditor.Views
{
    public sealed class Library : UserControl
    {
        private readonly TreeView _tree;

        public Library()
        {
            InitializeComponent();

            _tree = new()
            {
                SelectionMode = SelectionMode.Single,
                [Grid.RowProperty] = 1,
            };
            DataContext = new LibraryViewModel(_tree);
        }

        private new Grid Content => (Grid)base.Content;

        public void InitializeTreeView()
        {
            var treelist = new AvaloniaList<TreeViewItem>();
            _tree.Items = treelist;

            foreach (var item in EffectMetadata.LoadedEffects)
            {
                var treeitem = new TreeViewItem
                {
                    Header = item.Name,
                    DataContext = item,
                };
                treelist.Add(treeitem);
                treeitem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

                Add(treeitem, item);
            }

            Content.Children.RemoveAt(1);
            Content.Children.Add(_tree);
        }

        private void Add(TreeViewItem treeitem, EffectMetadata list)
        {
            if (list.Children is not null)
            {
                var alist = new AvaloniaList<TreeViewItem>();
                treeitem.Items = alist;
                foreach (var item in list.Children)
                {
                    var treeitem2 = new TreeViewItem
                    {
                        Header = item.Name,
                        DataContext = item,
                    };
                    alist.Add(treeitem2);
                    treeitem2.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

                    Add(treeitem2, item);
                }
            }
        }

        private async void TreeViewPointerPressed(object? s, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                if (s is not TreeViewItem select ||
                    select.DataContext is not EffectMetadata metadata ||
                    (metadata.Children?.Any() ?? false)) return;

                _tree.SelectedItem = select;
                await Task.Delay(10);

                var dataObject = new DataObject();
                dataObject.Set("EffectMetadata", metadata);

                // ドラッグ開始
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}