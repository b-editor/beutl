using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ViewModels;

namespace Beutl.Views.LibraryViews;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        LibraryTree.ContainerPrepared += OnItemContainerPrepared;
        LibraryTree.ContainerClearing += OnItemContainerClearing;
    }

    private void OnItemContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    private void OnItemContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is TreeViewItem treeItem)
        {
            treeItem.RemoveHandler(PointerPressedEvent, TreeViewPointerPressed);
        }
    }

    private async void TreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        LibraryItemViewModel? item;
        if (e.GetCurrentPoint(LibraryTree).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            LibraryTree.SelectedItem = item;
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
