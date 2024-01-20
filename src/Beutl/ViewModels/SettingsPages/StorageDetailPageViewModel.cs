using Avalonia.Collections;

using Beutl.Api.Objects;

using Beutl.Controls.Navigation;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.ExtensionsPages;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class StorageDetailPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<StorageDetailPageViewModel>();
    private readonly AuthorizedUser _user;

    public StorageDetailPageViewModel(AuthorizedUser user, StorageSettingsPageViewModel.KnownType type)
    {
        _user = user;
        Type = type;
        TypeString = StorageSettingsPageViewModel.ToDisplayName(type);

        Refresh.Subscribe(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("StorageDetailPage.Refresh");

            try
            {
                IsBusy.Value = true;
                using (await _user.Lock.LockAsync())
                {
                    activity?.AddEvent(new("Entered_AsyncLock"));
                    await _user.RefreshAsync();

                    Items.Clear();

                    int prevCount = 0;
                    int count = 0;

                    activity?.AddEvent(new("Start_GetAssetFiles"));

                    do
                    {
                        Asset[] items = await user.Profile.GetAssetsAsync(count, 30);

                        Items.AddRange(items.Where(x => StorageSettingsPageViewModel.ToKnownType(x.ContentType) == Type)
                            .Select(x => new AssetViewModel(x, x.Size.HasValue ? StringFormats.ToHumanReadableSize(x.Size.Value) : string.Empty)));
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);

                    activity?.AddEvent(new("Done_GetAssetFiles"));
                    activity?.SetTag("Assets_Count", Items.Count);
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                ErrorHandle(ex);
                _logger.LogError(ex, "An unexpected error has occurred.");
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

    public string TypeString { get; }

    public AvaloniaList<AssetViewModel> Items { get; } = [];

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public AsyncReactiveCommand NavigateParent { get; } = new();

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user);
    }

    public async Task DeleteAsync(AssetViewModel asset)
    {
        using Activity? activity = Telemetry.StartActivity("StorageDetailPage.Delete");

        using (await asset.Model.Lock.LockAsync())
        {
            activity?.AddEvent(new("Entered_AsyncLock"));

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
