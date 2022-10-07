using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

using static BeUtl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace BeUtl.ViewModels.SettingsPages.Dialogs;

public class SelectAvatarImageViewModel
{
    private readonly AuthorizedUser _user;

    public SelectAvatarImageViewModel(AuthorizedUser user)
    {
        _user = user;
        IsPrimaryButtonEnabled = IsBusy.CombineLatest(SelectedItem)
            .Select(x => !x.First && x.Second != null)
            .ToReadOnlyReactivePropertySlim();

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
                    Items.AddRange(items.Where(x => ToKnownType(x.ContentType) == KnownType.Image && x.IsPublic.Value));
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

    public AvaloniaList<Asset> Items { get; } = new();

    public ReactivePropertySlim<Asset?> SelectedItem { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPrimaryButtonEnabled { get; }

    public async Task SubmitAsync()
    {
        if (SelectedItem.Value != null)
        {
            await _user.Profile.UpdateAsync(new UpdateProfileRequest(
                SelectedItem.Value.DownloadUrl,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    public async Task<Asset> UploadImage(string path, string contentType)
    {
        try
        {
            IsBusy.Value = true;

            using FileStream stream = File.OpenRead(path);
            Asset asset = await _user.Profile.AddAssetAsync(Guid.NewGuid().ToString(), stream, contentType);
            await asset.UpdateAsync(new UpdateAssetRequest(true));

            Items.Add(asset);

            return asset;
        }
        finally
        {
            IsBusy.Value = false;
        }
    }
}
