using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab.ViewModels;

namespace Beutl.Editor.Components.LibraryTab.Views.LibraryViews;

/// <summary>
/// Drags an installed material out as a plain file, which is what the player and the
/// timeline already accept — no drop target needs to know about materials.
/// </summary>
internal static class MaterialTreeDragHelper
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

        if (!e.GetCurrentPoint(treeView).Properties.IsLeftButtonPressed) return;

        if ((e.Source as StyledElement)?.DataContext is not MaterialItemViewModel item) return;

        treeView.SelectedItem = item;
        if (!item.CanDragDrop) return;

        if (TopLevel.GetTopLevel(treeView) is not { StorageProvider: { } storageProvider }) return;

        IStorageFile? file = await storageProvider.TryGetFileFromPathAsync(item.FilePath!);
        if (file == null) return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }
}
