using Avalonia.Collections;
using Avalonia.Platform.Storage;

using Beutl.Api.Objects;

using BeUtl.Utilities;

using Reactive.Bindings;

using static BeUtl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace BeUtl.ViewModels.Dialogs;

public class SelectAssetViewModel
{
    private readonly AuthorizedUser _user;
    private readonly Func<string, bool> _contentTypeFilter;
    private readonly FilePickerFileType? _defaultFileType;

    public SelectAssetViewModel(AuthorizedUser user, Func<string, bool> contentTypeFilter, FilePickerFileType? defaultFileType = null)
    {
        _user = user;
        _contentTypeFilter = contentTypeFilter;
        _defaultFileType = defaultFileType;
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
                    Asset[] items = await _user.Profile.GetAssetsAsync(count, 30);
                    Items.AddRange(items.Where(x => _contentTypeFilter(x.ContentType))
                        .Select(x => new AssetViewModel(x, x.Size.HasValue ? StringFormats.ToHumanReadableSize(x.Size.Value) : string.Empty)));
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

    public AvaloniaList<AssetViewModel> Items { get; } = new();

    public ReactivePropertySlim<AssetViewModel?> SelectedItem { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPrimaryButtonEnabled { get; }

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user, _defaultFileType);
    }

    public record AssetViewModel(Asset Model, string UsedCapacity)
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
