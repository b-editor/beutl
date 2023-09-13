using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Beutl.ViewModels.ExtensionsPages.DevelopPages;
using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class DevelopPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<DevelopPageViewModel>();
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public DevelopPageViewModel(AuthorizedUser user, BeutlApiApplication apiApplication)
    {
        _user = user;
        DataContextFactory = new DataContextFactory(user, apiApplication);

        Refresh.Subscribe(async () =>
        {
            using Activity? activity = Services.Telemetry.StartActivity("DevelopPage.Refresh");

            try
            {
                IsBusy.Value = true;
                using (await _user.Lock.LockAsync())
                {
                    activity?.AddEvent(new("Entered_AsyncLock"));

                    await _user.RefreshAsync();

                    Packages.Clear();

                    int prevCount = 0;
                    int count = 0;

                    do
                    {
                        Package[] items = await _user.Profile.GetPackagesAsync(count, 30);
                        Packages.AddRange(items.AsSpan());
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);
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

        Refresh.Execute();
    }

    public CoreList<Package> Packages { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public DataContextFactory DataContextFactory { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
        Packages.Clear();

        GC.SuppressFinalize(this);
    }
}
