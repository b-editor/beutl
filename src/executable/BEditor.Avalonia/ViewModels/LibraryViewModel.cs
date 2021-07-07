using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls;

using BEditor.Data;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public sealed class LibraryViewModel
    {
        private readonly TreeView _tree;

        public LibraryViewModel(TreeView tree)
        {
            _tree = tree;
            SearchText.Subscribe(async str =>
            {
                await Task.Delay(100);
                if (string.IsNullOrWhiteSpace(str))
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
                    var regices = SearchService.CreateRegices(str);

                    if (_tree.Items is AvaloniaList<TreeViewItem> items)
                    {
                        foreach (var item in items)
                        {
                            item.IsVisible = SetIsVisible(item, str => SearchService.IsMatch(regices, str));
                        }
                    }
                }
            });
        }

        public ReactiveProperty<string> SearchText { get; } = new(string.Empty);

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
    }
}