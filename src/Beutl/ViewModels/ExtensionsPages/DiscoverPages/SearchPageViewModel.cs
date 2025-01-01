using Avalonia.Collections;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class SearchPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<SearchPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly DiscoverService _discoverService;

    public SearchPageViewModel(DiscoverService discoverService, string keyword)
    {
        _discoverService = discoverService;
        Keyword = keyword;

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("SearchPage.Refresh");

                try
                {
                    IsBusy.Value = true;
                    await RefreshPackages();
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        More = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Services.Telemetry.StartActivity("SearchPage.More");

                try
                {
                    IsBusy.Value = true;
                    await MoreLoadPackages();
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);
    }

    public string Keyword { get; }

    public AvaloniaList<object> Packages { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private async Task<Package[]> SearchPackages(int start, int count)
    {
        return await _discoverService.Search(Keyword, start, count);
    }

    private async Task RefreshPackages()
    {
        Packages.Clear();
        Packages.AddRange(Enumerable.Repeat(new DummyItem(), 6));

        using (await _discoverService.Lock.LockAsync())
        {
            Package[] array = await SearchPackages(0, 30);
            Packages.Clear();
            Packages.AddRange(array);

            if (array.Length == 30)
            {
                Packages.Add(new LoadMoreItem());
            }
        }
    }

    private async Task MoreLoadPackages()
    {
        using (await _discoverService.Lock.LockAsync())
        {
            Packages.RemoveAt(Packages.Count - 1);
            Package[] array = await SearchPackages(Packages.Count, 30);
            Packages.AddRange(array);

            if (array.Length == 30)
            {
                Packages.Add(new LoadMoreItem());
            }
        }
    }
}
