using Avalonia.Threading;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

internal sealed class StorageItemActivity
{
    private readonly List<Operation> _operations = [];
    public ReactivePropertySlim<bool> IsActive { get; } = new();
    public ReactivePropertySlim<bool> IsIndeterminate { get; } = new(true);
    public ReactivePropertySlim<double> Progress { get; } = new();

    public Operation Begin()
    {
        var operation = new Operation(this);
        _operations.Add(operation);
        Update();
        return operation;
    }

    private void Update()
    {
        double? progress = _operations.LastOrDefault()?.Progress;
        Progress.Value = progress ?? 0;
        IsIndeterminate.Value = !progress.HasValue;
        IsActive.Value = _operations.Count != 0;
    }

    internal sealed class Operation(StorageItemActivity owner) : IDisposable
    {
        private bool _disposed;
        internal double? Progress { get; private set; }

        public void Report(double? value)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Report(value));
                return;
            }
            if (_disposed) return;
            Progress = value is { } progress ? Math.Clamp(progress, 0, 100) : null;
            owner.Update();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._operations.Remove(this);
            owner.Update();
        }
    }
}

internal sealed class StorageItemOperation : IDisposable
{
    private readonly Dictionary<CloudStorageItem, StorageItemActivity.Operation> _items;

    public StorageItemOperation(IEnumerable<CloudStorageItem> items)
        => _items = items.Distinct().ToDictionary(item => item, item => item.Activity.Begin());

    public bool HasItems => _items.Count != 0;

    public void Report(CloudStorageItem item, double? progress)
    {
        if (_items.TryGetValue(item, out var operation)) operation.Report(progress);
    }

    public void Dispose()
    {
        foreach (var operation in _items.Values) operation.Dispose();
    }
}
