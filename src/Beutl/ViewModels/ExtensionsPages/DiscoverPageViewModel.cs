using Avalonia.Threading;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class DiscoverPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<DiscoverPageViewModel>();
    private readonly BeutlApiApplication _clients;
    private readonly DiscoverService _discoverService;

    public DiscoverPageViewModel(BeutlApiApplication clients)
    {
        static async Task LoadAsync(CoreList<object> packages, Func<int, int, Task<Package[]>> func, int maxCount = int.MaxValue)
        {
            int prevCount = 0;
            int count = 0;

            do
            {
                Package[] items = await func(count, 30).ConfigureAwait(false);
                if (count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(packages.Clear);
                }

                count += items.Length;

                if (maxCount < count)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => packages.AddRange(items.Take(count - maxCount)));
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() => packages.AddRange(items));
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
                // placeholder
                DummyItem[] dummy = Enumerable.Repeat(new DummyItem(), 6).ToArray();
                foreach (CoreList<object>? item in new[] { DailyRanking, WeeklyRanking, Top10, RecentlyRanking })
                {
                    item.Clear();
                    item.AddRange(dummy);
                }

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
                ErrorHandle(ex);
                _logger.LogError(ex, "An unexpected error has occurred.");
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public CoreList<object> Top10 { get; } = [];

    public CoreList<object> DailyRanking { get; } = [];

    public CoreList<object> WeeklyRanking { get; } = [];

    public CoreList<object> RecentlyRanking { get; } = [];

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public DataContextFactory DataContextFactory { get; }

    public override void Dispose()
    {
    }
}
