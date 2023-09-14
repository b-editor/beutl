using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class DiscoverPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<DiscoverPageViewModel>();
    private readonly BeutlApiApplication _clients;
    private readonly DiscoverService _discoverService;

    public DiscoverPageViewModel(BeutlApiApplication clients)
    {
        static async Task LoadAsync(CoreList<Package> packages, Func<int, int, Task<Package[]>> func, int maxCount = int.MaxValue)
        {
            packages.Clear();

            int prevCount = 0;
            int count = 0;

            do
            {
                Package[] items = await func(count, 30).ConfigureAwait(false);
                count += items.Length;

                if (maxCount < count)
                {
                    packages.AddRange(items.AsSpan<Package>().Slice(0, count - maxCount));
                }
                else
                {
                    packages.AddRange(items.AsSpan<Package>());
                    prevCount = items.Length;
                }
            } while (prevCount == 30 && maxCount > count);
        }

        _clients = clients;
        _discoverService = new DiscoverService(clients);
        DataContextFactory = new DataContextFactory(_discoverService, _clients);
        Refresh.Subscribe(async () =>
        {
            using Activity? activity = Services.Telemetry.StartActivity("DiscoverPage.Refresh");

            try
            {
                IsBusy.Value = true;
                AuthorizedUser? user = _clients.AuthorizedUser.Value;

                using (await _clients.Lock.LockAsync().ConfigureAwait(false))
                {
                    activity?.AddEvent(new("Entered_AsyncLock"));
                    if (user != null)
                    {
                        await user.RefreshAsync().ConfigureAwait(false);
                    }

                    Task task0 = Task.Run(() => LoadAsync(DailyRanking, (start, count) => _discoverService.GetDailyRanking(start, count), 10));
                    Task task1 = Task.Run(() => LoadAsync(WeeklyRanking, (start, count) => _discoverService.GetWeeklyRanking(start, count), 10));
                    Task task2 = Task.Run(() => LoadAsync(Top10, (start, count) => _discoverService.GetOverallRanking(start, count), 10));
                    Task task3 = Task.Run(() => LoadAsync(RecentlyRanking, (start, count) => _discoverService.GetRecentlyRanking(start, count), 10));
                    await Task.WhenAll(task0, task1, task2, task3).ConfigureAwait(false);
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

    public CoreList<Package> Top10 { get; } = new();

    public CoreList<Package> DailyRanking { get; } = new();

    public CoreList<Package> WeeklyRanking { get; } = new();

    public CoreList<Package> RecentlyRanking { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public DataContextFactory DataContextFactory { get; }

    public override void Dispose()
    {
    }
}
