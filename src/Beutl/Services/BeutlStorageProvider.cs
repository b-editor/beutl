using Beutl.Api;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.Services;

internal sealed class BeutlStorageProvider(INotificationService notifications,
    BeutlApiApplication clients,
    Func<SettingsDialogViewModel> createSettings) : IFileBrowserStorageProvider
{
    public string Id => "beutl";
    public string DisplayName => Strings.CloudStorage;

    public IFileBrowserStorageBrowser CreateBrowser() => new CloudStorageViewModel(notifications, clients, createSettings);
}
