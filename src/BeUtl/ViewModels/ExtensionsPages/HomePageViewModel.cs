using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Collections;

using Beutl.Api;
using Beutl.Api.Objects;

using DynamicData;

using FluentAvalonia.UI.Data;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public class HomePageViewModel : BasePageViewModel
{
    private readonly BeutlClients _clients;
    private readonly DiscoverService _discoverService;

    public HomePageViewModel(BeutlClients clients)
    {
        async Task LoadAsync(CoreList<Package> packages, Func<int, int, Task<Package[]>> func)
        {
            packages.Clear();

            int prevCount = 0;
            int count = 0;

            do
            {
                Package[] items = await func(count, 30);
                packages.AddRange(items.AsSpan<Package>());
                prevCount = items.Length;
                count += items.Length;
            } while (prevCount == 30);
        }

        _clients = clients;
        _discoverService = new DiscoverService(clients);
        Refresh.Subscribe(async () =>
        {
            try
            {
                IsBusy.Value = true;
                var user = _clients.AuthorizedUser.Value;
                if (user != null)
                {
                    await user.RefreshAsync();
                }

                await LoadAsync(DailyRanking, (start, count) => _discoverService.GetDailyRanking(start, count));
                await LoadAsync(WeeklyRanking, (start, count) => _discoverService.GetWeeklyRanking(start, count));
                await LoadAsync(OverallRanking, (start, count) => _discoverService.GetOverallRanking(start, count));
                await LoadAsync(RecentlyRanking, (start, count) => _discoverService.GetRecentlyRanking(start, count));
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        Refresh.Execute();
    }

    public CoreList<Package> DailyRanking { get; } = new();
    
    public CoreList<Package> WeeklyRanking { get; } = new();

    public CoreList<Package> OverallRanking { get; } = new();

    public CoreList<Package> RecentlyRanking { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public override void Dispose()
    {
    }
}
