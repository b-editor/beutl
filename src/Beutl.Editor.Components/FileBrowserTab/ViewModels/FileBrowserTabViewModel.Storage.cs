using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed partial class FileBrowserTabViewModel
{
    public IReadOnlyList<IFileBrowserStorageProvider> StorageProviders { get; private set; } = [];
    public bool HasStorageProviders => StorageProviders.Count > 0;
    public ReactivePropertySlim<IFileBrowserStorageProvider?> ActiveStorageProvider { get; } = new();
    public ReactivePropertySlim<IFileBrowserStorageBrowser?> StorageBrowser { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> IsStorageView { get; private set; } = null!;
    public ReadOnlyReactivePropertySlim<IFileBrowserStorageNavigation?> StorageNavigation { get; private set; } = null!;

    private void InitializeStorage(FileBrowserStorageProviderRegistry? registry)
    {
        StorageProviders = registry?.Providers ?? [];
        ActiveStorageProvider.DisposeWith(_disposables);
        StorageBrowser.DisposeWith(_disposables);
        IsStorageView = ActiveStorageProvider.Select(provider => provider != null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        StorageNavigation = StorageBrowser.Select(browser => browser as IFileBrowserStorageNavigation)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
    }

    public void OpenStorage(IFileBrowserStorageProvider provider)
    {
        if (_disposed
            || !StorageProviders.Any(item => ReferenceEquals(item, provider))
            || ReferenceEquals(ActiveStorageProvider.Value, provider))
            return;

        ShowLocalFiles();
        try
        {
            // Creating a file browser or restoring a layout never connects.
            // Only this explicit location selection creates the provider's browser.
            var browser = provider.CreateBrowser()
                ?? throw new InvalidOperationException("The storage provider did not create a browser.");
            if (_disposed)
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
