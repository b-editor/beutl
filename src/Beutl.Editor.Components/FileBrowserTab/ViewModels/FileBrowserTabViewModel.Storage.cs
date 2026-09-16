using Avalonia.Threading;
using Beutl.Configuration;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed partial class FileBrowserTabViewModel
{
    private ViewConfig _viewConfig = null!;

    public IReadOnlyList<IFileBrowserStorageProvider> StorageProviders { get; private set; } = [];
    public ReactivePropertySlim<bool> ShowStorageServices { get; } = new();
    public ReactivePropertySlim<IFileBrowserStorageProvider?> ActiveStorageProvider { get; } = new();
    public ReactivePropertySlim<IFileBrowserStorageBrowser?> StorageBrowser { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> IsStorageView { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<IFileBrowserStorageNavigation?> StorageNavigation { get; private set; } = null!;

    private void InitializeStorage(ViewConfig config, FileBrowserStorageProviderRegistry? registry)
    {
        _viewConfig = config;
        StorageProviders = registry?.Providers ?? [];
        ShowStorageServices.DisposeWith(_disposables);
        ActiveStorageProvider.DisposeWith(_disposables);
        StorageBrowser.DisposeWith(_disposables);
        IsStorageView = ActiveStorageProvider.Select(provider => provider != null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        StorageNavigation = StorageBrowser.Select(browser => browser as IFileBrowserStorageNavigation)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        config.GetObservable(ViewConfig.ShowStorageServicesProperty).Subscribe(_ =>
        {
            if (Dispatcher.UIThread.CheckAccess()) UpdateVisibility();
            else Dispatcher.UIThread.Post(UpdateVisibility);
        }).DisposeWith(_disposables);

        void UpdateVisibility()
        {
            if (_disposed) return;
            ShowStorageServices.Value = config.ShowStorageServices && StorageProviders.Count > 0;
            if (!ShowStorageServices.Value) ShowLocalFiles();
        }
    }

    public void OpenStorage(IFileBrowserStorageProvider provider)
    {
        if (_disposed || !_viewConfig.ShowStorageServices
            || !StorageProviders.Any(item => ReferenceEquals(item, provider))
            || ReferenceEquals(ActiveStorageProvider.Value, provider))
            return;

        ShowLocalFiles();
        try
        {
            // Creating a file browser, restoring a layout, or enabling the setting never connects.
            // Only this explicit location selection creates the provider's browser.
            var browser = provider.CreateBrowser()
                ?? throw new InvalidOperationException("The storage provider did not create a browser.");
            if (_disposed || !_viewConfig.ShowStorageServices)
            {
                browser.Dispose();
                return;
            }
            StorageBrowser.Value = browser;
            SelectedItems.Clear();
            ActiveStorageProvider.Value = provider;
        }
        catch (Exception ex)
        {
            ShowLocalFiles();
            _logger.LogError(ex, "Failed to open storage provider {ProviderId}.", provider.Id);
            Beutl.Services.NotificationService.ShowError(provider.DisplayName, Strings.StorageProviderOpenFailed);
        }
    }

    public void ShowLocalFiles()
    {
        if (!_disposed) CloseStorageBrowser();
    }

    private void CloseStorageBrowser()
    {
        var browser = StorageBrowser.Value;
        ActiveStorageProvider.Value = null;
        StorageBrowser.Value = null;
        try
        {
            browser?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose a storage browser.");
        }
    }
}
