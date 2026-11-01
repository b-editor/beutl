using Avalonia.Collections;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

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

        Refresh = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(() => StorePageCommand.RunAsync(
                IsBusy, "SearchPage.Refresh", _lifetimeCts.Token, _logger,
                _ => RefreshPackages(_lifetimeCts.Token)))
            .DisposeWith(_disposables);

        More = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(() => StorePageCommand.RunAsync(
                IsBusy, "SearchPage.More", _lifetimeCts.Token, _logger,
                _ => MoreLoadPackages(_lifetimeCts.Token)))
            .DisposeWith(_disposables);

        Kind = new PackageKindFilterViewModel(() => Refresh.Execute())
            .DisposeWith(_disposables);

        Refresh.Execute();
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
            Package[] array = await SearchPackages(0, PackagePageList.PageSize, cancellationToken);
            PackagePageList.ShowFirstPage(Packages, array);
        }
    }

    private async Task MoreLoadPackages(CancellationToken cancellationToken)
    {
        using (await _discoverService.Lock.LockAsync(cancellationToken))
        {
            Package[] array = await SearchPackages(
                PackagePageList.NextPageStart(Packages),
                PackagePageList.PageSize,
                cancellationToken);
            PackagePageList.AppendNextPage(Packages, array);
        }
    }
}
