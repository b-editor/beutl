using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

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
                Package[] items = await func(count, 30);
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
            try
            {
                IsBusy.Value = true;
                var user = _clients.AuthorizedUser.Value;
                
                using (await _clients.Lock.LockAsync())
                {
                    if (user != null)
                    {
                        await user.RefreshAsync();
                    }

                    await LoadAsync(DailyRanking, (start, count) => _discoverService.GetDailyRanking(start, count), 10);
                    await LoadAsync(WeeklyRanking, (start, count) => _discoverService.GetWeeklyRanking(start, count), 10);
                    await LoadAsync(Top10, (start, count) => _discoverService.GetOverallRanking(start, count), 10);
                    await LoadAsync(RecentlyRanking, (start, count) => _discoverService.GetRecentlyRanking(start, count), 10);
                }
            }
            catch (Exception ex)
            {
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
