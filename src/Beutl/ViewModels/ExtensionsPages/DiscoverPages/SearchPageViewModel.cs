using Avalonia.Collections;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class SearchPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<SearchPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
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
                    await RefreshPackages(_lifetimeCts.Token);
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
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
                    await MoreLoadPackages(_lifetimeCts.Token);
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
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

        Kind = new PackageKindFilterViewModel(() => Refresh.Execute())
            .DisposeWith(_disposables);
    }

    public string Keyword { get; }

    public PackageKindFilterViewModel Kind { get; }

    public AvaloniaList<object> Packages { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel the lifetime token during disposal.");
        }
        finally
        {
            _disposables.Dispose();
            _lifetimeCts.Dispose();
        }
    }

    private async Task<Package[]> SearchPackages(int start, int count, CancellationToken cancellationToken)
    {
        return await _discoverService.Search(Keyword, cancellationToken, start, count, Kind.Selected);
    }

    private async Task RefreshPackages(CancellationToken cancellationToken)
    {
        Packages.Clear();
        Packages.AddRange(Enumerable.Repeat(new DummyItem(), 6));

        using (await _discoverService.Lock.LockAsync(cancellationToken))
        {
            Package[] array = await SearchPackages(0, 30, cancellationToken);
            Packages.Clear();
            Packages.AddRange(array);

            if (array.Length == 30)
            {
                Packages.Add(new LoadMoreItem());
            }
        }
    }

    private async Task MoreLoadPackages(CancellationToken cancellationToken)
    {
        using (await _discoverService.Lock.LockAsync(cancellationToken))
        {
            Packages.RemoveAt(Packages.Count - 1);
            Package[] array = await SearchPackages(Packages.Count, 30, cancellationToken);
            Packages.AddRange(array);

            if (array.Length == 30)
            {
                Packages.Add(new LoadMoreItem());
            }
        }
    }
}
