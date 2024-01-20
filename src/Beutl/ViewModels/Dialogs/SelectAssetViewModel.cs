using Avalonia.Collections;
using Avalonia.Platform.Storage;

using Beutl.Api.Objects;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Utilities;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class SelectAssetViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<SelectAssetViewModel>();
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
            using Activity? activity = Telemetry.StartActivity("SelectAsset.Refresh");
            try
            {
                IsBusy.Value = true;
                using (await _user.Lock.LockAsync())
                {
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
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogError(ex, "An exception occurred while loading the list of assets.");
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public AvaloniaList<AssetViewModel> Items { get; } = [];

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
