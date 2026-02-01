using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Xaml.Interactivity;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

// ファイルブラウザアイテムのドラッグ開始を処理するBehavior。
// ListBoxItemやTreeViewItemに適用して使用する。
public class FileItemDragBehavior : Behavior<Control>
{
    private const double DragThreshold = 5;
    private Point? _dragStartPoint;
    private FileSystemItemViewModel? _dragItem;
    private bool _isDragStarting;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed += OnPointerPressed;
            AssociatedObject.PointerMoved += OnPointerMoved;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
            AssociatedObject.PointerMoved -= OnPointerMoved;
        }

        base.OnDetaching();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject == null)
            return;

        if (e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed
            && sender is Control { DataContext: FileSystemItemViewModel item })
        {
            _dragStartPoint = e.GetPosition(AssociatedObject);
            _dragItem = item;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint == null || _dragItem == null || _isDragStarting || AssociatedObject == null)
            return;

        if (!e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            _dragItem = null;
            return;
        }

        Point currentPos = e.GetPosition(AssociatedObject);
        double dx = currentPos.X - _dragStartPoint.Value.X;
        double dy = currentPos.Y - _dragStartPoint.Value.Y;

        if (Math.Abs(dx) <= DragThreshold && Math.Abs(dy) <= DragThreshold)
            return;

        if (TopLevel.GetTopLevel(AssociatedObject) is not { StorageProvider: IStorageProvider storageProvider })
            return;

        _isDragStarting = true;
        try
        {
            var data = new DataTransfer();

            // 選択中アイテムが複数あればすべて含める
            var vm = GetViewModel();
            var items = vm?.SelectedItems is { Count: > 1 } selectedItems
                        && selectedItems.Contains(_dragItem)
                ? selectedItems.ToList()
                : new List<FileSystemItemViewModel> { _dragItem };

            foreach (FileSystemItemViewModel item in items)
            {
                IStorageItem? storageItem = item.IsDirectory
                    ? await storageProvider.TryGetFolderFromPathAsync(item.FullPath) as IStorageItem
                    : await storageProvider.TryGetFileFromPathAsync(item.FullPath);

                if (storageItem != null)
                {
                    data.Add(DataTransferItem.CreateFile(storageItem));
                }
            }

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragStartPoint = null;
            _dragItem = null;
            _isDragStarting = false;
        }
    }

    private FileBrowserTabViewModel? GetViewModel()
    {
        // UserControl (FileBrowserTab) の DataContext から ViewModel を取得
        var parent = AssociatedObject;
        while (parent != null)
        {
            if (parent is UserControl { DataContext: FileBrowserTabViewModel vm })
                return vm;
            parent = parent.Parent as Control;
        }

        return null;
    }
}
