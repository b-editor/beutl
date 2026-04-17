using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Editor.Components.LibraryTab.ViewModels;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

internal static class LibraryTreeDragHelper
{
    public static void Attach(TreeView treeView)
    {
        treeView.ContainerPrepared += OnItemContainerPrepared;
        treeView.ContainerClearing += OnItemContainerClearing;
    }

    private static void OnItemContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.AddHandler(InputElement.PointerPressedEvent, OnTreeViewPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private static void OnItemContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.RemoveHandler(InputElement.PointerPressedEvent, OnTreeViewPointerPressed);
        }
    }

    private static async void OnTreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var treeView = (sender as Control)?.FindAncestorOfType<TreeView>();
        if (treeView == null) return;

        LibraryItemViewModel? item;
        if (e.GetCurrentPoint(treeView).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            treeView.SelectedItem = item;
        }
        else
        {
            return;
        }

        if (item != null)
        {
            (DataFormat<string>, Type)[] arr = item.TryDragDrop().ToArray();
            if (arr.Length > 0)
            {
                var data = new DataTransfer();
                foreach ((DataFormat<string> format, Type type) in arr)
                {
                    data.Add(DataTransferItem.Create(format, TypeFormat.ToString(type)));
                }

                await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
            }
        }
    }
}
