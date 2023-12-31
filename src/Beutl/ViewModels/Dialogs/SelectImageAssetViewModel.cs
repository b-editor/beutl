using Avalonia.Collections;
using Avalonia.Platform.Storage;

using Beutl.Api.Objects;
using Beutl.Services;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

using static Beutl.ViewModels.SettingsPages.StorageSettingsPageViewModel;

namespace Beutl.ViewModels.Dialogs;

public class SelectImageAssetViewModel
{
    private readonly AuthorizedUser _user;
    private readonly ILogger _logger = Log.ForContext<SelectImageAssetViewModel>();

    public SelectImageAssetViewModel(AuthorizedUser user)
    {
        _user = user;
        IsPrimaryButtonEnabled = IsBusy.CombineLatest(SelectedItem)
            .Select(x => !x.First && x.Second != null)
            .ToReadOnlyReactivePropertySlim();

        Refresh.Subscribe(async () =>
        {
            using Activity? activity = Telemetry.StartActivity("SelectImageAsset.Refresh");
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
                        Asset[] items = await user.Profile.GetAssetsAsync(count, 30);
                        Items.AddRange(items.Where(x => ToKnownType(x.ContentType) == KnownType.Image));
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(ex);
                _logger.Error(ex, "An exception occurred while loading the asset.");
                NotificationService.ShowError(Message.An_exception_occurred_while_loading_the_asset, ex.Message);
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public AvaloniaList<Asset> Items { get; } = [];

    public ReactivePropertySlim<Asset?> SelectedItem { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPrimaryButtonEnabled { get; }

    public CreateAssetViewModel CreateAssetViewModel()
    {
        return new CreateAssetViewModel(_user, FilePickerFileTypes.ImageAll);
    }
}
