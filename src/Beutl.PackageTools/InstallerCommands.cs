using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using Kokuban;

using Microsoft.Extensions.Logging;

using NuGet.Packaging.Core;
using NuGet.Versioning;

using Sharprompt;

namespace Beutl.PackageTools;

public sealed partial class InstallerCommands : ConsoleAppBase
{
    private readonly BeutlApiApplication _application;
    private readonly ILogger<InstallerCommands> _logger;
    private readonly PackageInstaller _installer;
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly DiscoverService _discover;

    public InstallerCommands(BeutlApiApplication application, ILogger<InstallerCommands> logger)
    {
        _application = application;
        _logger = logger;
        _installer = _application.GetResource<PackageInstaller>();
        _installedPackageRepository = _application.GetResource<InstalledPackageRepository>();
        _discover = _application.GetResource<DiscoverService>();
    }

    private static void Show(
        HashSet<(PackageIdentity, Release?)>? installs,
        HashSet<(PackageIdentity, Release?)>? updates,
        HashSet<(PackageIdentity, Release?)>? uninstalls)
    {
        if (installs != null)
        {
            Console.WriteLine($"\nInstalls: ");
            foreach ((PackageIdentity package, Release? release) in installs)
            {
                if (release == null)
                    Console.WriteLine($"\t{package} [ローカル]");
                else
                    Console.WriteLine($"\t{package} [リモート]");
            }
        }

        if (updates != null)
        {
            Console.WriteLine($"\nUpdates: ");
            foreach ((PackageIdentity package, Release? release) in updates)
            {
                if (release == null)
                    Console.WriteLine($"\t{package} [ローカル]");
                else
                    Console.WriteLine($"\t{package} [リモート]");
            }
        }

        if (uninstalls != null)
        {
            Console.WriteLine($"\nUninstalls: ");
            foreach ((PackageIdentity package, Release? _) in uninstalls)
            {
                Console.WriteLine($"\t{package}");
            }
        }
    }

    private async ValueTask LoadArgs(
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

    private async ValueTask<(PackageIdentity?, Release?)> TryParse(string s, bool forinstall)
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
                    Console.WriteLine("このパッケージは既に登録されています。");
                    if (!Prompt.Confirm("再度インストールしますか？", true))
                    {
                        return empty;
                    }
                }

                try
                {
                    Package package = await _discover.GetPackage(pkg.Id);
                    Release release = await package.GetReleaseAsync(splited[1]);

                    return (pkg, release);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"'{s}'を取得できませんでした。");
                    if (ex is BeutlApiException<ApiErrorResponse> apiEx)
                    {
                        Console.Error.WriteLine($"\tMessage:   {apiEx.Result.Message}");
                        Console.Error.WriteLine($"\tErrorCode: {apiEx.Result.Error_code}");
                    }

                    string localNupkgPath = Path.Combine(Helper.LocalSourcePath, $"{pkg}.nupkg");
                    if (File.Exists(localNupkgPath))
                    {
                        Console.WriteLine("同じIDを持つパッケージがローカルから見つかりました。");
                        Console.WriteLine(localNupkgPath);
                        if (Prompt.Confirm("このパッケージを使用しますか？", defaultValue: false))
                        {
                            return (pkg, null);
                        }
                    }
                }

                return empty;
            }
        }
        else if (splited.Length == 1)
        {
            if (!forinstall)
            {
                Console.Error.WriteLine(Chalk.Red[$"'{s}': バージョンを指定してください。"]);
                return empty;
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
                        Console.Error.WriteLine($"パッケージ:{package.Name}にはリリースがありません。");
                        return empty;
                    }
                }
                catch
                {
                    Console.Error.WriteLine($"パッケージ:{s}を取得できませんでした。");
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
