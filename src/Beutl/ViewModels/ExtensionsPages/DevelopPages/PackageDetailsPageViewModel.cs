using Beutl.Api.Objects;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.ForContext<PackageDetailsPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly AuthorizedUser _user;

    public PackageDetailsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Refresh.Subscribe(async () =>
        {
            if (IsBusy.Value)
                return;

            using Activity? activity = Services.Telemetry.StartActivity("PackageDetailsPage.Refresh");

            try
            {
                using (await _user.Lock.LockAsync())
                {
                    activity?.AddEvent(new("Entered_AsyncLock"));

                    IsBusy.Value = true;

                    await _user.RefreshAsync();

                    await Package.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(ex);
                ErrorHandle(ex);
                _logger.Error(ex, "An unexpected error has occurred.");
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        DisplayName = package.DisplayName
            .Select(x => !string.IsNullOrWhiteSpace(x) ? x : Package.Name)
            .ToReadOnlyReactivePropertySlim(Package.Name)
            .DisposeWith(_disposables);
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<string> DisplayName { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
