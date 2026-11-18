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

    public bool Conflict { get; init; }

    internal bool AllowUnsupportedVersion { get; init; }

    private static string GetLocalNupkgPath(PackageIdentity package)
    {
        return Path.Combine(Helper.LocalSourcePath, $"{package}.nupkg");
    }

    private static bool CheckLocalSource(PackageIdentity package)
    {
        string localNupkgPath = GetLocalNupkgPath(package);
        return File.Exists(localNupkgPath);
    }

    private static async Task<PackageChangeModel?> ReadLocalSource(
        PackageIdentity package,
        PackageChangeAction action,
        CancellationToken cancellationToken)
    {
        string localNupkgPath = GetLocalNupkgPath(package);
        s_logger.LogDebug("Reading local source for package {PackageId} at path {Path}", package.Id, localNupkgPath);
        if (File.Exists(localNupkgPath))
        {
            try
            {
                using var reader = new PackageArchiveReader(localNupkgPath);
                NuspecReader nuspec = await reader.GetNuspecReaderAsync(cancellationToken);
                s_logger.LogInformation("Successfully read local package {PackageId} from path {Path}", package.Id, localNupkgPath);
                return new PackageChangeModel(package.Id, package.Version, nuspec.GetTitle() ?? package.Id, false, action)
                {
                    Description = nuspec.GetDescription(),
                    Publisher = nuspec.GetAuthors()
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "An exception occurred while reading nupkg file for package {PackageId} at path {Path}", package.Id, localNupkgPath);
            }
        }
        else
        {
            s_logger.LogWarning("Local package {PackageId} not found at path {Path}", package.Id, localNupkgPath);
        }

        return null;
    }

    public static ValueTask<PackageChangeModel?> TryParse(
        BeutlApiApplication apiApp,
        string s,
        PackageChangeAction action,
        CancellationToken cancellationToken)
        => TryParse(apiApp, s, action, fromCommandLine: true, cancellationToken);

    internal static async ValueTask<PackageChangeModel?> TryParse(
        BeutlApiApplication apiApp, string s, PackageChangeAction action, bool fromCommandLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] parts = s.Split('/');
        // The CLI can force an install or a specific update. Editor hand-offs always check the target.
        bool force = fromCommandLine && (action == PackageChangeAction.Install || (action == PackageChangeAction.Update && parts.Length == 2));
        PackageChangeModel? model;

        if (parts.Length == 2)
        {
            model = await ResolveVersionedAsync(apiApp.GetResource<DiscoverService>(), parts[0], parts[1], action, cancellationToken);
        }
        else if (parts.Length == 1)
        {
            model = await ResolveLatestAsync(apiApp, s, action, force, cancellationToken);
        }
        else
        {
            s_logger.LogWarning("Invalid package identifier format: {PackageId}", s);
            return null;
        }

        return force && model != null ? model with { AllowUnsupportedVersion = true } : model;
    }

    // "<id>/<version>": the remote package, or the local nupkg / an uninstall-only entry when discovery fails.
    private static async ValueTask<PackageChangeModel?> ResolveVersionedAsync(
        DiscoverService discover,
        string id,
        string version,
        PackageChangeAction action,
        CancellationToken cancellationToken)
    {
        var pkg = new PackageIdentity(id, new NuGetVersion(version));
        PackageChangeModel? item = null;

        try
        {
            s_logger.LogDebug("Attempting to discover package {PackageId}", pkg.Id);
            Package package = await discover.GetPackage(pkg.Id, cancellationToken);
            s_logger.LogInformation("Successfully discovered package {PackageId}", pkg.Id);
            item = new PackageChangeModel(pkg.Id, pkg.Version, package.DisplayName.Value ?? pkg.Id, true, action)
            {
                LogoUrl = package.LogoUrl.Value,
                Publisher = package.Owner.Name,
                Description = package.ShortDescription.Value,
                Conflict = CheckLocalSource(pkg)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "An exception occurred while discovering package {PackageId}", pkg.Id);
            if (action == PackageChangeAction.Uninstall)
            {
                item = new PackageChangeModel(pkg.Id, pkg.Version, pkg.Id, false, action);
            }
            else if (CheckLocalSource(pkg))
            {
                item = await ReadLocalSource(pkg, action, cancellationToken);
            }
        }

        return item;
    }

    // An update by name selects a compatible upgrade. A versioned CLI request names its release explicitly.
    private static async ValueTask<PackageChangeModel?> ResolveLatestAsync(
        BeutlApiApplication apiApp,
        string s,
        PackageChangeAction action,
        bool force,
        CancellationToken cancellationToken)
    {
        try
        {
            s_logger.LogDebug("Attempting to discover package {PackageId}", s);
            Package package = await apiApp.GetResource<DiscoverService>().GetPackage(s, cancellationToken);
            Release? release;
            if (action != PackageChangeAction.Uninstall && !force)
            {
                NuGetVersion? installed = apiApp.GetResource<InstalledPackageRepository>().GetLatestLocalPackage(s)?.Version;
                release = await package.GetLatestCompatibleReleaseAsync(cancellationToken, installed);
            }
            else
            {
                release = (await package.GetReleasesAsync(cancellationToken, 0, 1)).FirstOrDefault();
            }

            if (release != null)
            {
                var pkg = new PackageIdentity(package.Name, new NuGetVersion(release.Version.Value));
                s_logger.LogInformation("Successfully discovered package {PackageId} with release version {Version}", package.Name, release.Version.Value);

                return new PackageChangeModel(pkg.Id, pkg.Version, package.DisplayName.Value ?? pkg.Id, true, action)
                {
                    LogoUrl = package.LogoUrl.Value,
                    Publisher = package.Owner.Name,
                    Description = package.ShortDescription.Value
                };
            }
            else
            {
                s_logger.LogInformation("No applicable release found for package {PackageId}.", s);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "An exception occurred while discovering package {PackageId}", s);
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
