using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<PackageReleasesPageViewModel>();
    private readonly AuthorizedUser _user;

    public PackageReleasesPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Refresh.Subscribe(async () =>
        {
            try
            {
                using (await _user.Lock.LockAsync())
                {
                    IsBusy.Value = true;
                    await _user.RefreshAsync();

                    await Package.RefreshAsync();
                    Items.Clear();

                    int prevCount = 0;
                    int count = 0;

                    do
                    {
                        Release[] items = await Package.GetReleasesAsync(count, 30);
                        Items.AddRange(items.AsSpan<Release>());
                        prevCount = items.Length;
                        count += items.Length;
                    } while (prevCount == 30);
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

    public Package Package { get; }

    public CoreList<Release> Items { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public async Task DeleteReleaseAsync(Release release)
    {
        try
        {
            using(await _user.Lock.LockAsync())
            {
                await _user.RefreshAsync();
                await release.DeleteAsync();
                Items.Remove(release);
            }
        }
        catch (Exception ex)
        {
            ErrorHandle(ex);
            _logger.Error(ex, "An unexpected error has occurred.");
        }
    }

    public override void Dispose()
    {
        Items.Clear();
    }
}
