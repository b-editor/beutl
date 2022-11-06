using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public class DataContextFactory
{
    private readonly AuthorizedUser _user;
    private readonly DiscoverService _discoverService;

    public DataContextFactory(AuthorizedUser user, BeutlApiApplication apiApplication)
    {
        _user = user;
        _discoverService = apiApplication.GetResource<DiscoverService>();
    }

    public AddReleaseDialogViewModel AddReleaseDialog(Package package)
    {
        return new AddReleaseDialogViewModel(_user, package);
    }

    public CreatePackageDialogViewModel CreatePackageDialog()
    {
        return new CreatePackageDialogViewModel(_user);
    }

    public UpdatePackageDialogViewModel UpdatePackageDialog()
    {
        return new UpdatePackageDialogViewModel(_user, _discoverService);
    }

    public PackageDetailsPageViewModel PackageDetailsPage(Package package)
    {
        return new PackageDetailsPageViewModel(_user, package);
    }

    public PackageReleasesPageViewModel PackageReleasesPage(Package package)
    {
        return new PackageReleasesPageViewModel(_user, package);
    }

    public PackageSettingsPageViewModel PackageSettingsPage(Package package)
    {
        return new PackageSettingsPageViewModel(_user, package);
    }

    public ReleasePageViewModel ReleasePage(Release release)
    {
        return new ReleasePageViewModel(_user, release);
    }
}
