using System.ComponentModel;
using Avalonia.Controls;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.ViewModels.Dock;

public class BeutlToolDockable : Tool, IDisposable
{
    private readonly IDisposable _isSelectedSubscription;
    private readonly IDisposable _headerSubscription;
    private bool _isDisposed;

    public BeutlToolDockable(IToolContext context, EditViewModel editViewModel)
    {
        ToolContext = context;
        EditViewModel = editViewModel;

        Id = CreateId(context);
        Title = ResolveTitle(context, context.TabTitle.Value);
        Context = context;
        CanClose = true;
        CanFloat = true;
        CanPin = true;
        CanDockAsDocument = false;

        IsSelected = context.IsSelected.Value;

        _headerSubscription = context.TabTitle
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
        if (_isDisposed) return;
        _isDisposed = true;
        PropertyChanged -= OnPropertyChanged;
        _headerSubscription.Dispose();
        _isSelectedSubscription.Dispose();
        ToolContext.Dispose();
        ToolContent = null;
    }

    // A tool context — a plugin's especially — can publish an empty header, and so can the
    // extension's own menu label, so walk down to something a user can read. Extension.Name is the
    // last resort because it defaults to the bare type name.
    private static string ResolveTitle(IToolContext context, string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
            return header;

        if (!string.IsNullOrWhiteSpace(context.Extension.MenuHeader))
            return context.Extension.MenuHeader;

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
