using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views
{
    public class Library : UserControl
    {
        private readonly TreeView _tree;

        public Library()
        {
            InitializeComponent();

            _tree = new()
            {
                SelectionMode = SelectionMode.Single,
            };
            _tree.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
        }

        public void InitializeTreeView()
        {
            var alist = new AvaloniaList<TreeViewItem>();
            _tree.Items = alist;
            foreach (var item in EffectMetadata.LoadedEffects)
            {
                var treeitem = new TreeViewItem
                {
                    Header = item.Name,
                    DataContext = item,
                };
                alist.Add(treeitem);

                if (item.Children is not null)
                {
                    Add(treeitem, item.Children);
                }
            }

            Content = new ScrollViewer
            {
                Content = _tree,
            };
        }

        private void Add(TreeViewItem treeitem, IEnumerable<EffectMetadata> list)
        {
            var alist = new AvaloniaList<TreeViewItem>();
            treeitem.Items = alist;
            foreach (var item in list)
            {
                var treeitem2 = new TreeViewItem
                {
                    Header = item.Name,
                    DataContext = item,
                };
                alist.Add(treeitem2);

                if (item.Children is not null)
                {
                    Add(treeitem2, item.Children);
                }
            }
        }

        private async void TreeViewPointerPressed(object? s, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                if (_tree.SelectedItem is not TreeViewItem select ||
                    select.DataContext is not EffectMetadata metadata ||
                    metadata.Type == null) return;

                await Task.Delay(10);

                var dataObject = new DataObject();
                dataObject.Set("EffectMetadata", metadata);

                // ドラッグ開始
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);

                _tree.SelectedItem = null;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}