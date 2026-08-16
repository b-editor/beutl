using Avalonia.Collections;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class DiscoverPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<DiscoverPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly DiscoverService _discover;
    private readonly BeutlApiApplication _apiApp;

    public DiscoverPageViewModel(BeutlApiApplication apiApp, EditorService editorService, ProjectService projectService)
    {
        _apiApp = apiApp;
        _discover = apiApp.GetResource<DiscoverService>();
        DataContextFactory = new DataContextFactory(_discover, apiApp, editorService, projectService);

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("DiscoverPage.Refresh");

                try
                {
                    IsBusy.Value = true;
                    Items.Clear();
                    Items.AddRange(Enumerable.Repeat(new DummyItem(), 10));

                    Package[] array = await LoadItems(0, 30, activity, _lifetimeCts.Token);
                    Items.Clear();
                    Items.AddRange(array);

                    if (array.Length == 30)
                    {
                        Items.Add(new LoadMoreItem());
                    }
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
                using Activity? activity = Telemetry.StartActivity("DiscoverPage.More");

                try
                {
                    IsBusy.Value = true;
                    Items.RemoveAt(Items.Count - 1);
                    Package[] array = await LoadItems(Items.Count, 30, activity, _lifetimeCts.Token);
                    Items.AddRange(array);

                    if (array.Length == 30)
                    {
                        Items.Add(new LoadMoreItem());
                    }
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

        Refresh.Execute();
    }

    public PackageKindFilterViewModel Kind { get; }

    public AvaloniaList<object> Items { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public DataContextFactory DataContextFactory { get; }

    private async Task<Package[]> LoadItems(
        int start,
        int count,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        using (await _discover.Lock.LockAsync(cancellationToken))
        {
            activity?.AddEvent(new("Entered_AsyncLock"));
            if (_apiApp.AuthenticatedUser.Value != null)
            {
                await _apiApp.AuthenticatedUser.Value.RefreshAsync(cancellationToken);
            }

            return await _discover.GetFeatured(cancellationToken, start, count, Kind.Selected);
        }
    }

    public override void Dispose()
    {
        _lifetimeCts.Cancel();
        _disposables.Dispose();
        _lifetimeCts.Dispose();
    }
}
