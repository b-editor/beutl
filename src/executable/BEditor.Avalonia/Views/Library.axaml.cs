using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;

using BEditor.Data;

namespace BEditor.Views
{
    public class Library : UserControl
    {
        private readonly TreeView _tree;
        private readonly ScrollViewer _scroll = new ScrollViewer
        {
            [Grid.RowProperty] = 1,
        };

        public Library()
        {
            InitializeComponent();

            _tree = new()
            {
                SelectionMode = SelectionMode.Single,
            };
            _tree.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
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

                Add(treeitem, item);
            }

            Content.Children.RemoveAt(1);
            Content.Children.Add(_scroll);
            _scroll.Content = _tree;
        }

        public async void TextBox_KeyDown(object s, KeyEventArgs e)
        {
            if (s is not TextBox tb) return;
            await Task.Delay(250);
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                if (_tree.Items is AvaloniaList<TreeViewItem> items)
                {
                    foreach (var item in items)
                    {
                        item.IsVisible = SetIsVisible(item, _ => true);
                    }
                }
            }
            else
            {
                var textUpper = tb.Text.ToUpperInvariant();
                var regexPattern = Regex.Replace(textUpper, ".", m =>
                {
                    var s = m.Value;
                    if (s.Equals("?"))
                    {
                        return ".";
                    }
                    else if (s.Equals("*"))
                    {
                        return ".*";
                    }
                    else
                    {
                        return Regex.Escape(s);
                    }
                });
                var regex = new Regex(regexPattern);

                if (_tree.Items is AvaloniaList<TreeViewItem> items)
                {
                    foreach (var item in items)
                    {
                        item.IsVisible = SetIsVisible(item, str =>
                        {
                            var upper = str.ToUpperInvariant();
                            return regex.IsMatch(upper) || upper.Contains(textUpper);
                        });
                    }
                }
            }
        }

        // 一つでもIsVisibleがtrueだったらtrueを返す
        private bool SetIsVisible(TreeViewItem treeitem, Func<string, bool> validate)
        {
            // IsVisible
            var result = false;
            if (treeitem.Items is AvaloniaList<TreeViewItem> list)
            {
                var v = false;
                foreach (var item in list)
                {
                    if (item.DataContext is EffectMetadata metadata)
                    {
                        item.IsVisible = validate(metadata.Name);
                    }
                    v |= item.IsVisible;

                    result |= SetIsVisible(item, validate);
                }

                if (treeitem.DataContext is EffectMetadata treeitemMetadata)
                {
                    v |= validate(treeitemMetadata.Name);
                }

                treeitem.IsVisible = v;
                result |= v;
            }

            return result;
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

                    Add(treeitem2, item);
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