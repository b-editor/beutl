using Beutl.Logging;
using Beutl.PackageTools.UI.ViewModels;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.PackageTools.UI.Models;

public enum PackageChangeAction
{
    Install,
    Uninstall,
    Update
}

public record PackageChangeModel(
    string Id,
    NuGetVersion Version,
    string DisplayName,
    bool IsRemote,
    PackageChangeAction Action)
{
    private static readonly ILogger s_logger = Log.CreateLogger<PackageChangeModel>();

    public string? LogoUrl { get; init; }

    public string? Description { get; init; }

    public string? Publisher { get; init; }

    public bool AlreadyInstalled { get; init; }

    public bool Conflict { get; init; }

    private static bool CheckLocalSource(PackageIdentity package)
    {
        string localNupkgPath = Path.Combine(Helper.LocalSourcePath, $"{package}.nupkg");
        return File.Exists(localNupkgPath);
    }

    private static async ValueTask<PackageChangeModel?> ReadLocalSource(PackageIdentity package, bool alreadyInstalled, PackageChangeAction action)
    {
        string localNupkgPath = Path.Combine(Helper.LocalSourcePath, $"{package}.nupkg");
        if (File.Exists(localNupkgPath))
        {
            try
            {
                using var reader = new PackageArchiveReader(localNupkgPath);
                NuspecReader nuspec = await reader.GetNuspecReaderAsync(default);
                return new PackageChangeModel(package.Id, package.Version, nuspec.GetTitle() ?? package.Id, false, action)
                {
                    Description = nuspec.GetDescription(),
                    Publisher = nuspec.GetAuthors(),
                    AlreadyInstalled = alreadyInstalled
                };
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "An exception occurred while reading nupkg file.");
            }
        }

        return null;
    }

    public static async ValueTask<PackageChangeModel?> TryParse(BeutlApiApplication apiApp, string s, PackageChangeAction action)
    {
        InstalledPackageRepository repos = apiApp.GetResource<InstalledPackageRepository>();
        DiscoverService discover = apiApp.GetResource<DiscoverService>();
        string[] splited = s.Split('/');

        if (splited.Length == 2)
        {
            var pkg = new PackageIdentity(splited[0], new NuGetVersion(splited[1]));
            bool alreadyInstalled = repos.ExistsPackage(pkg);
            PackageChangeModel? item = null;

            try
            {
                Package package = await discover.GetPackage(pkg.Id);
                item = new PackageChangeModel(pkg.Id, pkg.Version, package.DisplayName.Value ?? pkg.Id, true, action)
                {
                    AlreadyInstalled = alreadyInstalled,
                    LogoUrl = package.LogoUrl.Value,
                    Publisher = package.Owner.Name,
                    Description = package.ShortDescription.Value,
                    Conflict = CheckLocalSource(pkg)
                };
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "An exception occurred.");
                if (action == PackageChangeAction.Uninstall)
                {
                    item = new PackageChangeModel(pkg.Id, pkg.Version, pkg.Id, false, action);
                }
                else if (CheckLocalSource(pkg))
                {
                    item = await ReadLocalSource(pkg, alreadyInstalled, action);
                }
            }

            return item;
        }
        else if (splited.Length == 1)
        {
            try
            {
                Package package = await discover.GetPackage(s);
                Release[] releases = await package.GetReleasesAsync(0, 1);

                if (releases.Length > 0)
                {
                    Release release = releases[0];
                    var pkg = new PackageIdentity(package.Name, new NuGetVersion(release.Version.Value));
                    bool alreadyInstalled = repos.ExistsPackage(pkg);

                    return new PackageChangeModel(pkg.Id, pkg.Version, package.DisplayName.Value ?? pkg.Id, true, action)
                    {
                        AlreadyInstalled = alreadyInstalled,
                        LogoUrl = package.LogoUrl.Value,
                        Publisher = package.Owner.Name,
                        Description = package.ShortDescription.Value
                    };
                }
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "An exception occurred.");
            }
        }

        return null;
    }

    public ActionViewModel? CreateViewModel(BeutlApiApplication app, ChangesModel model)
    {
        return Action switch
        {
            PackageChangeAction.Install => new InstallViewModel(app, model, this),
            PackageChangeAction.Uninstall => new UninstallViewModel(app, model, this),
            PackageChangeAction.Update => new InstallViewModel(app, model, this),
            _ => null,
        };
    }
}
