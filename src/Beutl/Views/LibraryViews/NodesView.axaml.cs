using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.ViewModels;

namespace Beutl.Views.LibraryViews;

public partial class NodesView : UserControl
{
    public NodesView()
    {
        InitializeComponent();
        NodeTreeView.ContainerPrepared += OnItemContainerPrepared;
        NodeTreeView.ContainerClearing += OnItemContainerClearing;
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
        if (e.GetCurrentPoint(NodeTreeView).Properties.IsLeftButtonPressed)
        {
            item = (e.Source as StyledElement)?.DataContext as LibraryItemViewModel;
            NodeTreeView.SelectedItem = item;
        }
        else
        {
            return;
        }

        if (item != null)
        {
            (string, Type)[] arr = item.TryDragDrop().ToArray();
            if (arr.Length > 0)
            {
                var dataObject = new DataObject();
                foreach ((string format, Type type) in arr)
                {
                    dataObject.Set(format, type);
                }

                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }
    }

}
