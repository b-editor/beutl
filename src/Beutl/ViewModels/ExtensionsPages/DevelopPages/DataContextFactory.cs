using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public class DataContextFactory(AuthorizedUser user, BeutlApiApplication apiApplication)
{
    private readonly DiscoverService _discoverService = apiApplication.GetResource<DiscoverService>();

    public AddReleaseDialogViewModel AddReleaseDialog(Package package)
    {
        return new AddReleaseDialogViewModel(user, package);
    }

    public CreatePackageDialogViewModel CreatePackageDialog()
    {
        return new CreatePackageDialogViewModel(user, _discoverService);
    }

    public UpdatePackageDialogViewModel UpdatePackageDialog()
    {
        return new UpdatePackageDialogViewModel(user, _discoverService);
    }

    public PackageDetailsPageViewModel PackageDetailsPage(Package package)
    {
        return new PackageDetailsPageViewModel(user, package);
    }

    public PackageReleasesPageViewModel PackageReleasesPage(Package package)
    {
        return new PackageReleasesPageViewModel(user, package);
    }

    public PackageSettingsPageViewModel PackageSettingsPage(Package package)
    {
        return new PackageSettingsPageViewModel(user, package);
    }

    public ReleasePageViewModel ReleasePage(Release release)
    {
        return new ReleasePageViewModel(user, release);
    }
}
