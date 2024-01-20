using Beutl.Api.Objects;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<PackageDetailsPageViewModel>();
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
                ErrorHandle(ex);
                _logger.LogError(ex, "An unexpected error has occurred.");
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
