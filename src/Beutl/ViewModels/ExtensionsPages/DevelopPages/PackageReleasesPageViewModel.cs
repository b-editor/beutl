using Beutl.Api.Objects;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<PackageReleasesPageViewModel>();
    private readonly AuthorizedUser _user;

    public PackageReleasesPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Refresh.Subscribe(async () =>
        {
            using Activity? activity = Services.Telemetry.StartActivity("PackageReleasesPage.Refresh");

            try
            {
                Items.Clear();
                // placeholder
                Items.AddRange(Enumerable.Repeat(new DummyItem(), 6));

                using (await _user.Lock.LockAsync())
                {
                    activity?.AddEvent(new("Entered_AsyncLock"));

                    IsBusy.Value = true;
                    await _user.RefreshAsync();

                    await Package.RefreshAsync();

                    int prevCount = 0;
                    int count = 0;

                    do
                    {
                        await Task.Delay(1000);
                        Release[] items = await Package.GetReleasesAsync(count, 30);
                        if (count == 0)
                        {
                            Items.Clear();
                        }

                        Items.AddRange(items);
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);

                    activity?.AddEvent(new("Refreshed_Releases"));
                    activity?.SetTag("Releases_Count", Items.Count);
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

    public Package Package { get; }

    public CoreList<object> Items { get; } = [];

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public async Task DeleteReleaseAsync(Release release)
    {
        using Activity? activity = Services.Telemetry.StartActivity("PackageReleasesPage.DeleteRelease");

        try
        {
            using (await _user.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));

                await _user.RefreshAsync();

                await release.DeleteAsync();
                Items.Remove(release);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            ErrorHandle(ex);
            _logger.LogError(ex, "An unexpected error has occurred.");
        }
    }

    public override void Dispose()
    {
        Items.Clear();
    }
}
