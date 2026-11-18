using System.ComponentModel;
using Avalonia.Controls;
using Beutl.Api.Services;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Helpers;
using Beutl.Services;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dock;

public class BeutlToolDockable : Tool, IDisposable
{
    private readonly IDisposable _isSelectedSubscription;
    private readonly IDisposable _headerSubscription;
    private readonly IDisposable? _browserLoadingSubscription;
    private bool _showsLoadingIndicator;
    private bool _isDisposed;
    private readonly ToolUsageTracker _usage;

    public BeutlToolDockable(IToolContext context, EditViewModel editViewModel)
    {
        ToolContext = context;
        EditViewModel = editViewModel;

        Id = CreateId(context.Extension);
        Title = ResolveTitle(context, context.Header.Value);
        Icon = ExtensionGuard.Call(context.Extension, Notifications, context.Extension.GetIcon);
        Context = context;
        _usage = new ToolUsageTracker(context);
        CanClose = true;
        CanFloat = true;
        CanPin = true;
        CanDockAsDocument = false;

        IsSelected = context.IsSelected.Value;

        if (context is WebBrowserTabViewModel browser)
        {
            // Finishing a fast navigation cancels the pending reveal immediately.
            _browserLoadingSubscription = browser.IsLoading
                .Select(loading => loading
                    ? Observable.Return(true).Delay(TimeSpan.FromMilliseconds(200), UiThreadScheduler.Instance)
                    : Observable.Return(false))
                .Switch()
                .Subscribe(visible => ShowsLoadingIndicator = visible);
        }

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

        UnpinCommand.Subscribe(_ =>
        {
            if (Pinnable is { } pinnable) pinnable.IsPinned.Value = false;
        });

        PropertyChanged += OnPropertyChanged;
    }

    internal INotificationService? Notifications => ToolContext.GetService<INotificationService>()
        ?? EditViewModel?.GetService<INotificationService>();

    public IToolContext ToolContext { get; }

    /// <summary>Gets the tool when it can be pinned to its target, or <see langword="null"/>.</summary>
    /// <remarks>Unrelated to <c>CanPin</c>, which lets Dock auto-hide the tab.</remarks>
    public IPinnableToolContext? Pinnable => ToolContext as IPinnableToolContext;

    /// <summary>Gets the command the tab header runs to unpin the tool.</summary>
    public ReactiveCommandSlim UnpinCommand { get; } = new();

    /// <summary>Gets the icon the tab strip shows left of the title, or <see langword="null"/> for none.</summary>
    public FAIconSource? Icon { get; }

    public bool ShowsLoadingIndicator
    {
        get => _showsLoadingIndicator;
        private set => SetProperty(ref _showsLoadingIndicator, value);
    }

    public EditViewModel EditViewModel { get; }

    internal Control? ToolContent { get; set; }

    internal void RecordInteraction()
    {
        if (_isDisposed) return;
        _usage.Interact();
        EditViewModel?.Usage?.ActivateTool(this, _usage.Tool);
    }

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
        _browserLoadingSubscription?.Dispose();
        _isSelectedSubscription.Dispose();
        UnpinCommand.Dispose();
        _usage.Dispose();
        Control? content = ToolContent;
        ToolContent = null;
        try
        {
            if (content is IDisposable disposableContent
                && !ReferenceEquals(disposableContent, ToolContext))
            {
                disposableContent.Dispose();
            }
        }
        finally
        {
            ToolContext.Dispose();
        }
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

    internal static string CreateId(ToolTabExtension extension)
    {
        // Unique id per instance for CanMultiple tools, stable id for singletons.
        var typeName = extension.GetType().FullName ?? extension.Name;
        return extension.CanMultiple
            ? $"{typeName}#{Guid.NewGuid():N}"
            : typeName;
    }
}
