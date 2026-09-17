using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public sealed partial class CloudStorageView
{
    private PointerPressedEventArgs? _storagePress;
    private Point _storagePressPoint;
    private CloudStorageItem? _storagePressedItem;
    private StorageActionContext? _storageDrag;
    private bool _preparingDrag;
    private bool _nativeDrag;
    private ListBoxItem? _dropHighlight;
    internal Func<PointerPressedEventArgs, IDataTransfer, Task<DragDropEffects>>? DragStarter { get; set; }

    private void InitializeStorageDragDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnStorageDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => HighlightDrop(null));
        AddHandler(DragDrop.DropEvent, OnStorageDrop);
        AddHandler(PointerPressedEvent, OnStoragePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnStoragePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnStoragePointerReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _storageDrag != null && !_nativeDrag)
            { ResetStorageDrag(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    private ListBoxItem? StorageItemAt(Point point)
    {
        // Drag feedback can invalidate hit-test rendering before the next frame.
        // Resolve against the current realized layout, clipped to the list viewport.
        if (this.TranslatePoint(point, StorageItems) is not { } listPoint || !new Rect(StorageItems.Bounds.Size).Contains(listPoint)) return null;
        return StorageItems.GetRealizedContainers().OfType<ListBoxItem>().FirstOrDefault(container =>
            this.TranslatePoint(point, container) is { } local && new Rect(container.Bounds.Size).Contains(local));
    }

    private void HighlightDrop(ListBoxItem? container)
    {
        _dropHighlight?.Classes.Remove("storage-drop-target");
        _dropHighlight = container;
        _dropHighlight?.Classes.Add("storage-drop-target");
    }

    private string? StorageDropDestination(Point point, CloudStorageViewModel vm) =>
        StorageItemAt(point)?.DataContext is CloudStorageItem { IsFolder: true } item ? item.Id : vm.CurrentFolderId;

    private void OnStorageDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not CloudStorageViewModel vm) { e.DragEffects = DragDropEffects.None; return; }
        var point = e.GetPosition(this);
        bool allowed = vm.CanDrop(e.DataTransfer, StorageDropDestination(point, vm));
        e.DragEffects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        HighlightDrop(allowed && StorageItemAt(point)?.DataContext is CloudStorageItem { IsFolder: true } ? StorageItemAt(point) : null);
    }

    private async void OnStorageDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        HighlightDrop(null);
        if (DataContext is not CloudStorageViewModel vm) return;
        try { await vm.DropAsync(e.DataTransfer, StorageDropDestination(e.GetPosition(this), vm)); }
        catch (Exception ex) { vm.ReportActionError(ex); }
    }

    private void OnStoragePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_preparingDrag || _nativeDrag || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not CloudStorageItem item) return;
        _storagePress = e;
        _storagePressPoint = e.GetPosition(this);
        _storagePressedItem = item;
    }

    private async void OnStoragePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_storagePress == null || _storagePressedItem == null || _preparingDrag || _nativeDrag || DataContext is not CloudStorageViewModel vm) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { ResetStorageDrag(); return; }
        var point = e.GetPosition(this);
        if (_storageDrag == null)
        {
            if (Math.Abs(point.X - _storagePressPoint.X) < 5 && Math.Abs(point.Y - _storagePressPoint.Y) < 5) return;
            var items = StorageItems.SelectedItems?.Contains(_storagePressedItem) == true
                ? StorageItems.SelectedItems.OfType<CloudStorageItem>().ToArray() : [_storagePressedItem];
            _storageDrag = vm.CaptureActionContext(items);
            if (_storageDrag == null) { ResetStorageDrag(); return; }
            CancelPrefetchIntent();
            e.Pointer.Capture(this);
        }
        e.Handled = true;
        if (new Rect(Bounds.Size).Contains(point))
        {
            var target = StorageItemAt(point);
            bool allowed = target?.DataContext is CloudStorageItem { IsFolder: true } folder
                && _storageDrag.Items.All(item => item.Can("move") && item.Id != folder.Id);
            HighlightDrop(allowed ? target : null);
            Cursor = new Cursor(allowed ? StandardCursorType.DragMove : StandardCursorType.No);
        }
        else await BeginExternalStorageDragAsync(vm, _storageDrag, _storagePress);
    }

    private async void OnStoragePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_nativeDrag) return;
        var context = _storageDrag;
        string? target = _dropHighlight?.DataContext is CloudStorageItem folder ? folder.Id : null;
        bool move = context != null && _dropHighlight != null && !_preparingDrag;
        ResetStorageDrag();
        if (move && DataContext is CloudStorageViewModel vm)
        {
            e.Handled = true;
            try { await vm.MoveDroppedEntriesAsync(context!, target); }
            catch (Exception ex) { vm.ReportActionError(ex); }
        }
    }

    private async Task BeginExternalStorageDragAsync(CloudStorageViewModel vm, StorageActionContext context, PointerPressedEventArgs trigger)
    {
        if (context.Items.Any(item => !item.IsFolder && !item.Can("download"))) { ResetStorageDrag(); return; }
        _preparingDrag = true;
        string directory = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "storage", "downloads", Guid.NewGuid().ToString("N"));
        bool retained = false;
        var handles = new List<IStorageItem>();
        try
        {
            var paths = await vm.ExportStorageItemsAsync(context, directory);
            if (paths == null || !_attached || !ReferenceEquals(_storageDrag, context) || !vm.IsActionCurrent(context)) return;
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return;
            using var data = new DataTransfer();
            foreach (string path in paths)
            {
                IStorageItem? item = Directory.Exists(path) ? await provider.TryGetFolderFromPathAsync(path) : await provider.TryGetFileFromPathAsync(path);
                if (item == null) throw new IOException("The downloaded file cannot be shared.");
                handles.Add(item);
                data.Add(DataTransferItem.CreateFile(item));
            }
            if (!_attached || !ReferenceEquals(_storageDrag, context) || !vm.IsActionCurrent(context)) return;
            data.Add(DataTransferItem.Create(StorageDragData.Format, new StorageDragData("beutl", context.User,
                context.Items.Select(x => new StorageDragEntry(x.Id, x.Name, x.IsFolder)).ToArray(), paths,
                () => vm.IsActionCurrent(context), destination => vm.MoveDroppedEntriesAsync(context, destination), vm)));
            _nativeDrag = true;
            trigger.Pointer.Capture(null);
            var effect = DragStarter != null ? await DragStarter(trigger, data) : await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy);
            // Native consumers may finish reading after the drag callback. Successful exports
            // are durable downloads; editor drops copy them into scene-owned resources.
            retained = effect != DragDropEffects.None;
        }
        catch (Exception ex) { if (vm.IsTransferCurrent(context)) vm.ReportActionError(ex); }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
            try { if (!retained && Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException ex) { if (vm.IsTransferCurrent(context)) vm.ReportActionError(ex); }
            catch (UnauthorizedAccessException ex) { if (vm.IsTransferCurrent(context)) vm.ReportActionError(ex); }
            _preparingDrag = _nativeDrag = false;
            ResetStorageDrag();
        }
    }

    private void ResetStorageDrag()
    {
        if (_preparingDrag && DataContext is CloudStorageViewModel vm) vm.CancelTransfer.Execute();
        _storagePress?.Pointer.Capture(null);
        _storagePress = null;
        _storagePressedItem = null;
        _storageDrag = null;
        HighlightDrop(null);
        Cursor = null;
    }
}
