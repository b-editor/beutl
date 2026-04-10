using System.ComponentModel;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.ViewModels.Dock;

public class BeutlToolDockable : Tool, IDisposable
{
    private readonly IDisposable _isSelectedSubscription;
    private bool _isDisposed;

    public BeutlToolDockable(IToolContext context, EditViewModel editViewModel)
    {
        ToolContext = context;
        EditViewModel = editViewModel;
        Icon = context.Extension.GetIcon();

        Id = CreateId(context);
        Title = context.Header;
        Context = context;
        CanClose = true;
        CanFloat = true;
        CanPin = true;

        IsActive = context.IsSelected.Value;

        _isSelectedSubscription = context.IsSelected
            .DistinctUntilChanged()
            .Subscribe(v =>
            {
                if (_isDisposed) return;
                if (IsActive != v) IsActive = v;
            });

        PropertyChanged += OnPropertyChanged;
    }

    public IToolContext ToolContext { get; }

    public EditViewModel EditViewModel { get; }

    public IconSource? Icon { get; }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed) return;
        if (e.PropertyName != nameof(IsActive)) return;

        if (ToolContext.IsSelected.Value != IsActive)
            ToolContext.IsSelected.Value = IsActive;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        PropertyChanged -= OnPropertyChanged;
        _isSelectedSubscription.Dispose();
        ToolContext.Dispose();
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
