using Avalonia.Collections;
using Avalonia.Platform.Storage;

using Beutl.Api.Objects;

using Reactive.Bindings;

using static Beutl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace Beutl.ViewModels.Dialogs;

public class SelectImageAssetViewModel
{
    private readonly AuthorizedUser _user;

    public SelectImageAssetViewModel(AuthorizedUser user)
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
                    Items.AddRange(items.Where(x => ToKnownType(x.ContentType) == KnownType.Image));
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

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user, FilePickerFileTypes.ImageAll);
    }
}
