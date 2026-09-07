using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.ViewModels.Dock;

internal static class ToolContextDisposal
{
    private static readonly AsyncLocal<WeakReference<IToolContext>?> s_current = new();

    public static bool IsCurrent(IToolContext context)
        => s_current.Value is { } current
            && current.TryGetTarget(out IToolContext? target)
            && ReferenceEquals(target, context);

    public static bool IsActive
        => s_current.Value is { } current && current.TryGetTarget(out _);

    public static async ValueTask DisposeAsync(IToolContext context)
    {
        if (IsCurrent(context))
            return;

        WeakReference<IToolContext>? previous = s_current.Value;
        s_current.Value = new WeakReference<IToolContext>(context);
        try
        {
            await context.DisposeAsync();
        }
        finally
        {
            s_current.Value = previous;
        }
    }
}

internal class BeutlToolDockable : Tool, IAsyncDisposable
{
    private IDisposable? _isSelectedSubscription;
    private IDisposable? _headerSubscription;
    private readonly object _disposeGate = new();
    private IToolContext? _toolContext;
    private Task? _disposeTask;
    private bool _isDisposed;

    public BeutlToolDockable(IToolContext context, EditViewModel editViewModel)
    {
        _toolContext = context;
        EditViewModel = editViewModel;

        Id = CreateId(context);
        Title = ResolveTitle(context, context.Header.Value);
        Context = context;
        CanClose = true;
        CanFloat = true;
        CanPin = true;
        CanDockAsDocument = false;

        IsSelected = context.IsSelected.Value;

        _headerSubscription = context.Header
            .DistinctUntilChanged()
            .Subscribe(v =>
            {
                if (_isDisposed) return;
                string title = ResolveTitle(context, v);
                if (Title != title) Title = title;
            });

        _isSelectedSubscription = context.IsSelected
            .DistinctUntilChanged()
            .Subscribe(v =>
            {
                if (_isDisposed) return;
                if (IsSelected != v) IsSelected = v;
            });

        PropertyChanged += OnPropertyChanged;
    }

    public IToolContext ToolContext
    {
        get
        {
            lock (_disposeGate)
                return _toolContext ?? throw new ObjectDisposedException(nameof(BeutlToolDockable));
        }
    }

    internal bool TryGetToolContext([NotNullWhen(true)] out IToolContext? context)
    {
        lock (_disposeGate)
        {
            context = _toolContext;
            return context is not null;
        }
    }

    public EditViewModel EditViewModel { get; }

    internal Control? ToolContent { get; set; }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed) return;
        if (e.PropertyName != nameof(IsSelected)) return;

        if (ToolContext.IsSelected.Value != IsSelected)
            ToolContext.IsSelected.Value = IsSelected;
    }

    /// <summary>Disposes this dockable and its owned tool context exactly once.</summary>
    /// <remarks>The returned task completes only after the context's asynchronous disposal completes.</remarks>
    public ValueTask DisposeAsync() => new(GetDisposeTask());

    internal Task GetDisposeTask()
    {
        TaskCompletionSource? completion = null;
        Task task;
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _isDisposed = true;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            task = completion.Task;
        }

        _ = CompleteDisposeAsync(completion);
        return task;
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    internal Task PendingDisposeTask
    {
        get
        {
            lock (_disposeGate)
                return _disposeTask ?? Task.CompletedTask;
        }
    }

    private async Task DisposeCoreAsync()
    {
        IToolContext? context;
        lock (_disposeGate)
            context = _toolContext;
        List<Exception>? errors = null;
        try
        {
            try { PropertyChanged -= OnPropertyChanged; }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { Interlocked.Exchange(ref _headerSubscription, null)?.Dispose(); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { Interlocked.Exchange(ref _isSelectedSubscription, null)?.Dispose(); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { ToolContent = null; }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try { Context = null; }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            try
            {
                if (context is not null)
                    await ToolContextDisposal.DisposeAsync(context);
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        finally
        {
            lock (_disposeGate)
                _toolContext = null;
        }
        if (errors is { Count: > 0 })
            throw new AggregateException(errors);
    }

    // Resolve empty per-instance/menu headers to a readable display or extension name.
    private static string ResolveTitle(IToolContext context, string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
            return header;

        if (!string.IsNullOrWhiteSpace(context.Extension.Header))
            return context.Extension.Header;

        return string.IsNullOrWhiteSpace(context.Extension.DisplayName)
            ? context.Extension.Name
            : context.Extension.DisplayName;
    }

    private static string CreateId(IToolContext context)
    {
        // Unique id per instance for CanMultiple tools, stable id for singletons.
        var typeName = context.Extension.GetType().FullName ?? context.Extension.Name;
        return context.Extension.CanMultiple
            ? $"{typeName}#{Guid.NewGuid():N}"
            : typeName;
    }
}
