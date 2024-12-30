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
    private readonly DiscoverService _discover;

    public DiscoverPageViewModel(BeutlApiApplication apiApp)
    {
        _discover = apiApp.GetResource<DiscoverService>();
        DataContextFactory = new DataContextFactory(_discover, apiApp);

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("DiscoverPage.Refresh");

                try
                {
                    IsBusy.Value = true;
                    Items.Clear();
                    Items.AddRange(Enumerable.Repeat(new DummyItem(), 10));

                    Package[] array = await LoadItems(0, 30, activity);
                    Items.Clear();
                    Items.AddRange(array);

                    if (array.Length == 30)
                    {
                        Items.Add(new LoadMoreItem());
                    }
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
                    Package[] array = await LoadItems(Items.Count, 30, activity);
                    Items.AddRange(array);

                    if (array.Length == 30)
                    {
                        Items.Add(new LoadMoreItem());
                    }
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

        Refresh.Execute();
    }

    public AvaloniaList<object> Items { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public DataContextFactory DataContextFactory { get; }

    private async Task<Package[]> LoadItems(int start, int count, Activity? activity)
    {
        using (await _discover.Lock.LockAsync())
        {
            activity?.AddEvent(new("Entered_AsyncLock"));
            return await _discover.GetFeatured(start, count);
        }
    }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
