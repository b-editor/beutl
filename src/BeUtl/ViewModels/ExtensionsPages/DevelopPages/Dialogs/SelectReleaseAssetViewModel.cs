using Avalonia.Collections;
using Avalonia.Platform.Storage;

using Beutl.Api.Objects;

using BeUtl.ViewModels.SettingsPages.Dialogs;

using Reactive.Bindings;

using static BeUtl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

public class SelectReleaseAssetViewModel
{
    private readonly AuthorizedUser _user;
    private readonly Release _release;

    public SelectReleaseAssetViewModel(AuthorizedUser user, Release release)
    {
        _user = user;
        _release = release;
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
                    Items.AddRange(items.Where(x => ToKnownType(x.ContentType) == KnownType.BeutlPackageFile)
                        .Select(x => new AssetViewModel(x, x.Size.HasValue ? ToHumanReadableSize(x.Size.Value) : string.Empty)));
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
        // Todo: FileTypeをStaticに移動
        return new CreateAssetViewModel(_user, new FilePickerFileType("Beutl Package File")
        {
            MimeTypes = new string[] { "application/x-beutl-package" },
            Patterns = new string[] { "*.bpkg" }
        });
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
