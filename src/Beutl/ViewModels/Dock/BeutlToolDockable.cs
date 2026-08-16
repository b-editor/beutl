using System.ComponentModel;
using Avalonia.Controls;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.ViewModels.Dock;

public class BeutlToolDockable : Tool, IDisposable
{
    private readonly IDisposable _isSelectedSubscription;
    private readonly IDisposable _headerSubscription;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private bool _isDisposed;

    public BeutlToolDockable(IToolContext context, EditViewModel editViewModel)
    {
        ToolContext = context;
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

    public IToolContext ToolContext { get; }

    public EditViewModel EditViewModel { get; }

    internal Control? ToolContent { get; set; }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed) return;
        if (e.PropertyName != nameof(IsSelected)) return;

        if (ToolContext.IsSelected.Value != IsSelected)
            ToolContext.IsSelected.Value = IsSelected;
    }

    public void Dispose()
    {
        _ = DisposeAsync();
    }

    internal Task DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _isDisposed = true;
            return _disposeTask = DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        PropertyChanged -= OnPropertyChanged;
        _headerSubscription.Dispose();
        _isSelectedSubscription.Dispose();
        ToolContent = null;

        if (ToolContext is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            ToolContext.Dispose();
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
