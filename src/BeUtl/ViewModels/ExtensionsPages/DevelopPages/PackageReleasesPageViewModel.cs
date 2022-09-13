using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageReleasesPageViewModel : IDisposable
{
    private readonly AuthorizedUser _user;

    public PackageReleasesPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Refresh.Subscribe(async () =>
        {
            try
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
            catch
            {
                // Todo
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

    public AddReleaseDialogViewModel CreateAddReleaseDialog()
    {
        return new AddReleaseDialogViewModel(_user, Package);
    }

    public PackageDetailsPageViewModel CreatePackageDetailsPage()
    {
        return new PackageDetailsPageViewModel(_user, Package);
    }

    public ReleasePageViewModel CreateReleasePage(Release release)
    {
        return new ReleasePageViewModel(_user, release);
    }

    public async Task DeleteReleaseAsync(Release release)
    {
        try
        {
            await _user.RefreshAsync();
            await release.DeleteAsync();
            Items.Remove(release);
        }
        catch
        {
            // Todo
        }
    }

    public void Dispose()
    {
        Items.Clear();

        GC.SuppressFinalize(this);
    }
}
