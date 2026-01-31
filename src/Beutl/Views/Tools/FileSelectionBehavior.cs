using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

// PointerReleased, KeyUpが発火した後，SelectedItemsから存在しないアイテムを削除するBehavior
public class FileSelectionBehavior : Behavior<Control>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.KeyDownEvent, OnKeyDown, handledEventsToo: true);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }

        base.OnDetaching();
    }

    private static bool ContainsRecursive(IEnumerable<FileSystemItemViewModel> items, FileSystemItemViewModel target)
    {
        foreach (var item in items)
        {
            if (item == target)
                return true;
            if (item.Children != null && ContainsRecursive(item.Children, target))
                return true;
        }

        return false;
    }

    private void Clean()
    {
        if (AssociatedObject?.DataContext is not FileBrowserTabViewModel viewModel)
            return;

        // SelectedItemsにsender.Itemsに含まれないものがあれば削除する
        var items = AssociatedObject switch
        {
            ListBox lb => lb.ItemsSource as ObservableCollection<FileSystemItemViewModel>,
            TreeView tree => tree.ItemsSource as ObservableCollection<FileSystemItemViewModel>,
            _ => null
        };
        if (items == null)
            return;

        for (int index = viewModel.SelectedItems.Count - 1; index >= 0; index--)
        {
            FileSystemItemViewModel item = viewModel.SelectedItems[index];
            if (!ContainsRecursive(items, item))
            {
                viewModel.SelectedItems.RemoveAt(index);
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        Clean();
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        Clean();
    }
}
