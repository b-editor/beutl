using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BEditorNext.ProjectSystem;

namespace BEditorNext.Views;

public partial class Library : UserControl
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

        foreach (RenderOperationRegistry.BaseRegistryItem item in RenderOperationRegistry.GetRegistered())
        {
            var treeitem = new TreeViewItem
            {
                [!HeaderedItemsControl.HeaderProperty] = new DynamicResourceExtension(item.DisplayName.Key),
                DataContext = item,
            };
            treelist.Add(treeitem);
            treeitem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);

            if (item is RenderOperationRegistry.GroupableRegistryItem groupable)
            {
                Add(treeitem, groupable);
            }
        }
    }

    private void Add(TreeViewItem treeitem, RenderOperationRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<TreeViewItem>();
        treeitem.Items = alist;
        foreach (RenderOperationRegistry.RegistryItem item in list.Items)
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
                select.DataContext is not RenderOperationRegistry.RegistryItem item) return;

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

    // ˆê‚Â‚Å‚àIsVisible‚ªtrue‚¾‚Á‚½‚çtrue‚ð•Ô‚·
    private bool SetIsVisible(TreeViewItem treeitem, Func<string, bool> validate)
    {
        // IsVisible
        bool result = false;
        bool v = false;
        if (treeitem.Items is AvaloniaList<TreeViewItem> list)
        {
            foreach (TreeViewItem? item in list)
            {
                if (item.DataContext is RenderOperationRegistry.BaseRegistryItem itemContext)
                {
                    item.IsVisible = validate(itemContext.DisplayName.FindResourceOrDefault(string.Empty));
                }
                v |= item.IsVisible;

                result |= SetIsVisible(item, validate);
            }
        }

        if (treeitem.DataContext is RenderOperationRegistry.BaseRegistryItem treeItemContext)
        {
            v |= validate(treeItemContext.DisplayName.FindResourceOrDefault(string.Empty));
        }

        treeitem.IsVisible = v;
        result |= v;

        return result;
    }
}
