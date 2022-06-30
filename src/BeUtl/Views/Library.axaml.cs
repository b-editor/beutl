using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.ProjectSystem;

namespace BeUtl.Views;

public sealed partial class Library : UserControl
{
    public Library()
    {
        InitializeComponent();
        SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);
        InitializeTreeView();
    }

    private void InitializeTreeView()
    {
        var treelist = new AvaloniaList<TreeViewItem>();
        Tree.Items = treelist;

        foreach (LayerOperationRegistry.BaseRegistryItem item in LayerOperationRegistry.GetRegistered())
        {
            var treeitem = new TreeViewItem
            {
                [!HeaderedItemsControl.HeaderProperty] = new DynamicResourceExtension(item.DisplayName.Key),
                DataContext = item,
            };
            treelist.Add(treeitem);
            treeitem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

            if (item is LayerOperationRegistry.GroupableRegistryItem groupable)
            {
                Add(treeitem, groupable);
            }
        }
    }

    private void Add(TreeViewItem treeitem, LayerOperationRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<TreeViewItem>();
        treeitem.Items = alist;
        foreach (LayerOperationRegistry.RegistryItem item in list.Items)
        {
            var treeitem2 = new TreeViewItem
            {
                [!HeaderedItemsControl.HeaderProperty] = new DynamicResourceExtension(item.DisplayName.Key),
                DataContext = item,
            };
            alist.Add(treeitem2);
            treeitem2.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private async void TreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
        {
            if (sender is not TreeViewItem select ||
                select.DataContext is not LayerOperationRegistry.RegistryItem item)
            {
                return;
            }

            Tree.SelectedItem = select;
            await Task.Delay(10);

            var dataObject = new DataObject();
            dataObject.Set("RenderOperation", item);

            await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
        }
    }

    private async void SearchQueryChanged(string str)
    {
        await Task.Delay(100);
        if (string.IsNullOrWhiteSpace(str))
        {
            if (Tree.Items is AvaloniaList<TreeViewItem> items)
            {
                foreach (TreeViewItem item in items)
                {
                    item.IsVisible = SetIsVisible(item, _ => true);
                }
            }
        }
        else
        {
            Regex[] regices = RegexHelper.CreateRegices(str);

            if (Tree.Items is AvaloniaList<TreeViewItem> items)
            {
                foreach (TreeViewItem item in items)
                {
                    item.IsVisible = SetIsVisible(item, str => RegexHelper.IsMatch(regices, str));
                }
            }
        }
    }

    // 一つでもIsVisibleがtrueだったらtrueを返す
    private bool SetIsVisible(TreeViewItem treeitem, Func<string, bool> validate)
    {
        // IsVisible
        bool result = false;
        bool v = false;
        if (treeitem.Items is AvaloniaList<TreeViewItem> list)
        {
            foreach (TreeViewItem? item in list)
            {
                if (item.DataContext is LayerOperationRegistry.BaseRegistryItem itemContext)
                {
                    item.IsVisible = validate(itemContext.DisplayName.FindOrDefault(string.Empty));
                }
                v |= item.IsVisible;

                result |= SetIsVisible(item, validate);
            }
        }

        if (treeitem.DataContext is LayerOperationRegistry.BaseRegistryItem treeItemContext)
        {
            v |= validate(treeItemContext.DisplayName.FindOrDefault(string.Empty));
        }

        treeitem.IsVisible = v;
        result |= v;

        return result;
    }
}
