using System.Windows.Input;
using Avalonia.Controls;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab;

/// <summary>A storage service offered as a location in the file browser.</summary>
public interface IFileBrowserStorageProvider
{
    /// <summary>A stable, unique identifier, independent of the display language.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Creates an independent browser only when the user opens this service.</summary>
    IFileBrowserStorageBrowser CreateBrowser();
}

/// <summary>
/// Owns a service's authentication, listing state, and pending operations. Disposing the browser
/// must cancel pending work and release account subscriptions. A new view may be created when docked
/// content is recreated; views must share this browser's state without owning its lifetime.
/// </summary>
public interface IFileBrowserStorageBrowser : IDisposable
{
    ICommand Refresh { get; }

    Control CreateView();
}

/// <summary>Optional navigation presented in the file browser's existing toolbar.</summary>
public interface IFileBrowserStorageNavigation
{
    IReadOnlyList<FileBrowserStorageBreadcrumb> Breadcrumbs { get; }
    IReadOnlyReactiveProperty<FileBrowserViewMode> ViewMode { get; }
    ICommand CycleViewMode { get; }
    Task NavigateToAsync(FileBrowserStorageBreadcrumb breadcrumb);
}

public sealed record FileBrowserStorageBreadcrumb(string Name, string? FolderId);
