using Avalonia.Collections;

using Beutl.Api.Objects;

using Beutl.Controls.Navigation;
using Beutl.Utilities;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.SettingsPages;

public sealed class StorageDetailPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<StorageDetailPageViewModel>();
    private readonly AuthorizedUser _user;

    public StorageDetailPageViewModel(AuthorizedUser user, StorageSettingsPageViewModel.KnownType type)
    {
        _user = user;
        Type = type;

        Refresh.Subscribe(async () =>
        {
            try
            {
                IsBusy.Value = true;
                using(await _user.Lock.LockAsync())
                {
                    await _user.RefreshAsync();

                    Items.Clear();

                    int prevCount = 0;
                    int count = 0;

                    do
                    {
                        Asset[] items = await user.Profile.GetAssetsAsync(count, 30);
                        Items.AddRange(items.Where(x => StorageSettingsPageViewModel.ToKnownType(x.ContentType) == Type)
                            .Select(x => new AssetViewModel(x, x.Size.HasValue ? StringFormats.ToHumanReadableSize(x.Size.Value) : string.Empty)));
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);
                }
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
                _logger.Error(ex, "An unexpected error has occurred.");
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();

        NavigateParent.Subscribe(async () =>
        {
            INavigationProvider nav = await GetNavigation();
            await nav.NavigateAsync<StorageSettingsPageViewModel>();
        });
    }

    public StorageSettingsPageViewModel.KnownType Type { get; }

    public AvaloniaList<AssetViewModel> Items { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public AsyncReactiveCommand NavigateParent { get; } = new();

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user);
    }

    public async Task DeleteAsync(AssetViewModel asset)
    {
        using (await asset.Model.Lock.LockAsync())
        {
            await asset.Model.DeleteAsync();
            Items.Remove(asset);
        }
    }

    public override void Dispose()
    {
    }

    public record AssetViewModel(Asset Model, string UsedCapacity)
    {
        public bool Physical => Model.AssetType == Api.AssetType.Physical;

        public bool Virtual => Model.AssetType == Api.AssetType.Virtual;

        public string ShortUrl
        {
            get
            {
                var uri = new Uri(Model.DownloadUrl);
                return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
            }
        }
    }
}
