using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public class DataContextFactory
{
    private readonly AuthorizedUser _user;

    public DataContextFactory(AuthorizedUser user)
    {
        _user = user;
    }

    public AddReleaseDialogViewModel AddReleaseDialog(Package package)
    {
        return new AddReleaseDialogViewModel(_user, package);
    }

    public CreatePackageDialogViewModel CreatePackageDialog()
    {
        return new CreatePackageDialogViewModel(_user);
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
