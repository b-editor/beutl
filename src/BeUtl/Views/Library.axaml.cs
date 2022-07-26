using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Streaming;

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

        foreach (OperatorRegistry.BaseRegistryItem item in OperatorRegistry.GetRegistered())
        {
            var treeitem = new TreeViewItem
            {
                [!HeaderedItemsControl.HeaderProperty] = new DynamicResourceExtension(item.DisplayName.Key),
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
                [!HeaderedItemsControl.HeaderProperty] = new DynamicResourceExtension(item.DisplayName.Key),
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
        if (e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed
            && sender is TreeViewItem select
            && select.DataContext is OperatorRegistry.RegistryItem item)
        {
            Tree.SelectedItem = select;
            await Task.Delay(10);

            var dataObject = new DataObject();
            dataObject.Set("StreamOperator", item);

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
                if (item.DataContext is OperatorRegistry.BaseRegistryItem itemContext)
                {
                    item.IsVisible = validate(itemContext.DisplayName.FindOrDefault(string.Empty));
                }
                else if (item.DataContext is OperatorRegistry.BaseRegistryItem itemContext2)
                {
                    item.IsVisible = validate(itemContext2.DisplayName.FindOrDefault(string.Empty));
                }
                v |= item.IsVisible;

                result |= SetIsVisible(item, validate);
            }
        }

        if (treeitem.DataContext is OperatorRegistry.BaseRegistryItem treeItemContext2)
        {
            v |= validate(treeItemContext2.DisplayName.FindOrDefault(string.Empty));
        }

        treeitem.IsVisible = v;
        result |= v;

        return result;
    }
}
