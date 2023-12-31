using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public class PackageIdArgumentParser(BeutlApiApplication apiApp)
{
    private readonly DiscoverService _discover = apiApp.GetResource<DiscoverService>();
    private readonly InstalledPackageRepository _installedPackageRepository = apiApp.GetResource<InstalledPackageRepository>();

    public async ValueTask LoadArgs(
        HashSet<(PackageIdentity, Release?)> packages,
        string[] items, bool forinstall, CancellationToken cancellationToken)
    {
        foreach (string s in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (PackageIdentity? pkg, Release? release) = await TryParse(s, forinstall);
            if (pkg != null)
                packages.Add((pkg, release));
        }
    }

    private static (PackageIdentity?, Release?) CheckLocalSource(PackageIdentity pkg, (PackageIdentity?, Release?) fallback)
    {
        string localNupkgPath = Path.Combine(Helper.LocalSourcePath, $"{pkg}.nupkg");
        if (File.Exists(localNupkgPath))
        {
            Console.WriteLine();
            Console.WriteLine(Resources.APackageWithTheSameIDWasFoundInTheLocalSource);
            Console.WriteLine(localNupkgPath);
            if (Prompt.Confirm(Resources.DoYouWantToUseThisPackage, defaultValue: false))
            {
                return (pkg, null);
            }
        }

        return fallback;
    }

    public async ValueTask<(PackageIdentity?, Release?)> TryParse(string s, bool forinstall)
    {
        string[] splited = s.Split('/');
        var empty = ((PackageIdentity?)null, (Release?)null);
        if (splited.Length == 2)
        {
            var pkg = new PackageIdentity(splited[0], new NuGetVersion(splited[1]));
            if (!forinstall)
            {
                return (pkg, null);
            }
            else
            {
                if (_installedPackageRepository.ExistsPackage(pkg))
                {
                    Console.WriteLine(pkg);
                    Console.WriteLine(Resources.ThisPackageIsAlreadyRegistered);
                    if (!Prompt.Confirm(Resources.DoYouWantToReinstallIt, true))
                    {
                        return empty;
                    }
                }

                try
                {
                    Package package = await _discover.GetPackage(pkg.Id);
                    Release release = await package.GetReleaseAsync(splited[1]);

                    return CheckLocalSource(pkg, (pkg, release));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.CouldNotGetXXX, s)]);
                    if (ex is BeutlApiException<ApiErrorResponse> apiEx)
                    {
                        Console.Error.WriteLine(Chalk.BrightRed[$"\tMessage:   {apiEx.Result.Message}"]);
                        Console.Error.WriteLine(Chalk.BrightRed[$"\tErrorCode: {apiEx.Result.Error_code}"]);
                    }

                    return CheckLocalSource(pkg, empty);
                }
            }
        }
        else if (splited.Length == 1)
        {
            if (!forinstall)
            {
                var pkg = new PackageIdentity(s, null);
                return (pkg, null);
            }
            else
            {
                try
                {
                    Package package = await _discover.GetPackage(s);
                    Release[] releases = await package.GetReleasesAsync(0, 1);
                    if (releases.Length > 0)
                    {
                        Release release = releases[0];
                        var pkg = new PackageIdentity(package.Name, new NuGetVersion(release.Version.Value));
                        return (pkg, release);
                    }
                    else
                    {
                        Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.XXXHasNoRelease, package.Name)]);
                        return empty;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.CouldNotGetXXX, s)]);
                    if (ex is BeutlApiException<ApiErrorResponse> apiEx)
                    {
                        Console.Error.WriteLine(Chalk.BrightRed[$"\tMessage:   {apiEx.Result.Message}"]);
                        Console.Error.WriteLine(Chalk.BrightRed[$"\tErrorCode: {apiEx.Result.Error_code}"]);
                    }
                    return empty;
                }
            }
        }
        else
        {
            return empty;
        }
    }
}
