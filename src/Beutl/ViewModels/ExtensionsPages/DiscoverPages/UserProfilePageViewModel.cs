using Avalonia.Collections;

using Beutl.Api.Objects;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class UserProfilePageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<UserProfilePageViewModel>();
    private readonly CompositeDisposable _disposables = [];

    public UserProfilePageViewModel(Profile profile)
    {
        Profile = profile;
        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("UserProfilePage.Refresh");

                try
                {
                    Packages.Clear();
                    Packages.AddRange(Enumerable.Repeat(new DummyItem(), 6));

                    using (await Profile.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        IsBusy.Value = true;
                        await Profile.RefreshAsync();
                        await RefreshPackages();
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

        More = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    using (await Profile.Lock.LockAsync())
                    {
                        await MoreLoadPackages();
                    }
                }
                catch (Exception e)
                {
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

    public Profile Profile { get; }

    public AvaloniaList<object> Packages { get; } = [];

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand More { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private async Task RefreshPackages()
    {
        Package[] array = await Profile.GetPackagesAsync(0, 30);
        Packages.Clear();
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(new LoadMoreItem());
        }
    }

    private async Task MoreLoadPackages()
    {
        Packages.RemoveAt(Packages.Count - 1);
        Package[] array = await Profile.GetPackagesAsync(Packages.Count, 30);
        Packages.AddRange(array);

        if (array.Length == 30)
        {
            Packages.Add(new LoadMoreItem());
        }
    }
}
