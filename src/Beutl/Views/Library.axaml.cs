using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.NodeTree;
using Beutl.Operation;

namespace Beutl.Views;

public sealed partial class Library : UserControl
{
    public Library()
    {
        InitializeComponent();
        //SearchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);
        InitializeNodeTreeView();
        InitializeOperatorTreeView();
    }

    private void InitializeNodeTreeView()
    {
        var treelist = new AvaloniaList<TreeViewItem>();
        NodeTreeView.Items = treelist;

        foreach (NodeRegistry.BaseRegistryItem item in NodeRegistry.GetRegistered())
        {
            var treeitem = new TreeViewItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };
            treelist.Add(treeitem);
            treeitem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

            if (item is NodeRegistry.GroupableRegistryItem groupable)
            {
                Add(treeitem, groupable);
            }
        }
    }

    private void Add(TreeViewItem treeitem, NodeRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<TreeViewItem>();
        treeitem.Items = alist;
        foreach (NodeRegistry.BaseRegistryItem item in list.Items)
        {
            var treeitem2 = new TreeViewItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };

            if (item is NodeRegistry.GroupableRegistryItem inner)
            {
                Add(treeitem2, inner);
            }
            else
            {
                treeitem2.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
            }
            alist.Add(treeitem2);
        }
    }

    private void InitializeOperatorTreeView()
    {
        var treelist = new AvaloniaList<TreeViewItem>();
        OperatorTree.Items = treelist;

        foreach (OperatorRegistry.BaseRegistryItem item in OperatorRegistry.GetRegistered())
        {
            var treeitem = new TreeViewItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };
            treelist.Add(treeitem);
            treeitem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

            if (item is OperatorRegistry.GroupableRegistryItem groupable)
            {
                Add(treeitem, groupable);
            }
        }
    }

    private void Add(TreeViewItem treeitem, OperatorRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<TreeViewItem>();
        treeitem.Items = alist;
        foreach (OperatorRegistry.BaseRegistryItem item in list.Items)
        {
            var treeitem2 = new TreeViewItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };

            if (item is OperatorRegistry.GroupableRegistryItem inner)
            {
                Add(treeitem2, inner);
            }
            else
            {
                treeitem2.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
            }
            alist.Add(treeitem2);
        }
    }

    private async void TreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TreeViewItem select)
        {
            var dataObject = new DataObject();
            if (e.GetCurrentPoint(OperatorTree).Properties.IsLeftButtonPressed
                && select.DataContext is OperatorRegistry.RegistryItem item)
            {
                OperatorTree.SelectedItem = select;
                await Task.Delay(10);

                dataObject.Set("SourceOperator", item);
            }
            else if (e.GetCurrentPoint(NodeTreeView).Properties.IsLeftButtonPressed
                && select.DataContext is NodeRegistry.RegistryItem item2)
            {
                NodeTreeView.SelectedItem = select;
                await Task.Delay(10);

                dataObject.Set("Node", item2);
            }
            else
            {
                return;
            }

            await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
        }
    }

    //private async void SearchQueryChanged(string? str)
    //{
    //    await Task.Delay(100);
    //    if (string.IsNullOrWhiteSpace(str))
    //    {
    //        if (OperatorTree.Items is AvaloniaList<TreeViewItem> items)
    //        {
    //            foreach (TreeViewItem item in items)
    //            {
    //                item.IsVisible = await SetIsVisible(item, _ => true);
    //            }
    //        }
    //        if (NodeTreeView.Items is AvaloniaList<TreeViewItem> items2)
    //        {
    //            foreach (TreeViewItem item in items2)
    //            {
    //                item.IsVisible = await SetIsVisible(item, _ => true);
    //            }
    //        }
    //    }
    //    else
    //    {
    //        Regex[] regices = RegexHelper.CreateRegices(str);

    //        if (OperatorTree.Items is AvaloniaList<TreeViewItem> items)
    //        {
    //            foreach (TreeViewItem item in items)
    //            {
    //                item.IsVisible = await SetIsVisible(item, str => RegexHelper.IsMatch(regices, str));
    //            }
    //        }
    //        if (NodeTreeView.Items is AvaloniaList<TreeViewItem> items2)
    //        {
    //            foreach (TreeViewItem item in items2)
    //            {
    //                item.IsVisible = await SetIsVisible(item, str => RegexHelper.IsMatch(regices, str));
    //            }
    //        }
    //    }
    //}

    // 一つでもIsVisibleがtrueだったらtrueを返す
    //private async ValueTask<bool> SetIsVisible(TreeViewItem treeitem, Func<string, bool> validate)
    //{
    //    // IsVisible
    //    bool result = false;
    //    bool v = false;
    //    if (treeitem.Items is AvaloniaList<TreeViewItem> list)
    //    {
    //        foreach (TreeViewItem? item in list)
    //        {
    //            if (item.DataContext is OperatorRegistry.BaseRegistryItem itemContext)
    //            {
    //                item.IsVisible = validate(itemContext.DisplayName ?? string.Empty);
    //            }
    //            else if (item.DataContext is OperatorRegistry.BaseRegistryItem itemContext2)
    //            {
    //                item.IsVisible = validate(itemContext2.DisplayName ?? string.Empty);
    //            }
    //            else if (item.DataContext is NodeRegistry.BaseRegistryItem itemContext3)
    //            {
    //                item.IsVisible = validate(itemContext3.DisplayName ?? string.Empty);
    //            }
    //            else if (item.DataContext is NodeRegistry.BaseRegistryItem itemContext4)
    //            {
    //                item.IsVisible = validate(itemContext4.DisplayName ?? string.Empty);
    //            }
    //            v |= item.IsVisible;

    //            result |= await SetIsVisible(item, validate);
    //        }
    //    }

    //    if (treeitem.DataContext is OperatorRegistry.BaseRegistryItem treeItemContext2)
    //    {
    //        v |= validate(treeItemContext2.DisplayName ?? string.Empty);
    //    }
    //    else if (treeitem.DataContext is NodeRegistry.BaseRegistryItem treeItemContext3)
    //    {
    //        v |= validate(treeItemContext3.DisplayName ?? string.Empty);
    //    }

    //    treeitem.IsVisible = v;
    //    result |= v;

    //    return result;
    //}
}
