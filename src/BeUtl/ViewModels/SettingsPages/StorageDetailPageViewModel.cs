using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Collections;

using Beutl.Api.Objects;

using BeUtl.ViewModels.SettingsPages.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public class StorageDetailPageViewModel
{
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
                await _user.RefreshAsync();

                Items.Clear();

                int prevCount = 0;
                int count = 0;

                do
                {
                    Asset[] items = await user.Profile.GetAssetsAsync(count, 30);
                    Items.AddRange(items.Where(x => StorageSettingsPageViewModel.ToKnownType(x.ContentType) == Type)
                        .Select(x => new AssetViewModel(x, x.Size.HasValue ? StorageSettingsPageViewModel.ToHumanReadableSize(x.Size.Value) : string.Empty)));
                    prevCount = items.Length;
                    count += items.Length;
                } while (prevCount == 30);
            }
            catch
            {
                // Todo
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public StorageSettingsPageViewModel.KnownType Type { get; }

    public AvaloniaList<AssetViewModel> Items { get; } = new();

    public AsyncReactiveCommand<AssetViewModel> Delete { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user);
    }

    public record AssetViewModel(Asset Model, string DisplayUsing)
    {
        public bool Physical => Model.AssetType == Beutl.Api.AssetType.Physical;

        public bool Virtual => Model.AssetType == Beutl.Api.AssetType.Virtual;

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
