using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.Logging;
using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class InstallCommand : Command
{
    private readonly ILogger _logger = Log.CreateLogger<InstallCommand>();
    private readonly Argument<string[]> _installs;
    private readonly Option<bool> _verbose;
    private readonly Option<bool> _clean;
    private readonly BeutlApiApplication _apiApp;

    public InstallCommand(BeutlApiApplication apiApp, Option<bool> verbose, Option<bool> clean)
        : base("install", Resources.InstallCommandDescription)
    {
        AddArgument(_installs = new Argument<string[]>(() => Array.Empty<string>())
        {
            Description = Resources.InstallsDescription,
            Arity = ArgumentArity.ZeroOrMore
        });

        _apiApp = apiApp;
        _verbose = verbose;
        _clean = clean;
        this.SetHandler(InvokeAsync);
    }

    private async Task InvokeAsync(InvocationContext context)
    {
        try
        {
            CancellationToken cancellationToken = context.GetCancellationToken();

            await WaitForProcessExited.Guard(cancellationToken);
            var commands = new InstallerCommands(_apiApp, cancellationToken);
            var parser = new PackageIdArgumentParser(_apiApp);

            string[] installs = context.ParseResult.GetValueForArgument(_installs)!;
            bool verbose = context.ParseResult.GetValueForOption(_verbose);
            bool clean = context.ParseResult.GetValueForOption(_clean);

            var installItems = new HashSet<(PackageIdentity, Release?)>();
            await parser.LoadArgs(installItems, installs, true, cancellationToken);

            PackageDisplay.Show(installItems, null, null);

            Console.WriteLine(Resources.PerformTheAboveOperations);
            if (!Prompt.Confirm(Resources.AreYouReady, defaultValue: true))
            {
                return;
            }
            else
            {
                await commands.InstallPackages(installItems, verbose, _logger);
                if (clean)
                {
                    commands.CleanPackages();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(Resources.OperationCanceled);
        }
    }
}

public partial class InstallerCommands
{
    public async Task InstallPackages(HashSet<(PackageIdentity, Release?)> items, bool verbose, ILogger logger)
    {
        foreach ((PackageIdentity package, Release? release) in items)
        {
            logger.LogInformation(
                "Install the package. ({PackageId}/{Version})",
                package.Id,
                release?.Version?.Value ?? package.Version.ToString());

            Console.WriteLine();
            string? nupkgPath = null;

            try
            {
                Console.WriteLine(Resources.InstallingXXX, package);

                PackageInstallContext? context;
                if (release != null)
                {
                    context = await _installer.PrepareForInstall(release, true, _cancellationToken);

                    string message = Resources.DownloadingAPackage;
                    await Spinner.StartAsync(message, async spinner =>
                    {
                        var progress = new KurukuruProgress(spinner, message);
                        await _installer.DownloadPackageFile(context, progress, _cancellationToken);
                    });
                    nupkgPath = context.NuGetPackageFile;

                    message = Resources.VerifyingHashValues;
                    await Spinner.StartAsync(message, async spinner =>
                    {
                        var progress = new KurukuruProgress(spinner, message);
                        await _installer.VerifyPackageFile(context, progress, _cancellationToken);
                    });

                    if (!context.HashVerified)
                    {
                        Console.Error.WriteLine(Chalk.BrightRed[Resources.InvalidHashValue]);
                        Console.Error.WriteLine(Chalk.BrightRed[Resources.ThisPackageFileMayHaveBeenTamperedWith]);
                        logger.LogWarning(
                            "Verify failed. ({PackageId}/{Version})",
                            package.Id,
                            release?.Version?.Value ?? package.Version.ToString());

                        if (!Prompt.Confirm(Resources.AreYouSureYouWantToContinueWithTheInstallation, false))
                        {
                            return;
                        }
                    }
                }
                else
                {
                    context = _installer.PrepareForInstall(package.Id, package.Version.ToString(), true, _cancellationToken);
                    Console.WriteLine(Resources.PackageDownloadWasOmitted);
                }

                // 依存関係を解決する
                await Spinner.StartAsync(Resources.ResolvingDependencies, async spinner =>
                {
                    await _installer.ResolveDependencies(context, verbose ? ConsoleLogger.Instance : NullLogger.Instance, _cancellationToken);
                });

                // 同意が必要なライセンスの内、未同意のものがある場合、同意させる
                var licensesRequiringApproval = context.LicensesRequiringApproval
                    .Where(x => !_acceptedLicenseManager.Accepted.ContainsKey(x.Item1))
                    .ToArray();
                if (licensesRequiringApproval.Length > 0)
                {
                    PackageDisplay.ShowLicenses(licensesRequiringApproval);
                    if (!Prompt.Confirm(Resources.PleaseAcceptTheAboveLicense))
                    {
                        // 同意しなかった場合、何もしない
                        return;
                    }
                }

                // 同意したことを記録
                _acceptedLicenseManager.Accepts(licensesRequiringApproval);

                // インストールされているパッケージのリストに追加
                _installedPackageRepository.AddPackage(package);
                _installedPackageRepository.UpgradePackages(package);

                Console.WriteLine(Chalk.BrightGreen[string.Format(Resources.InstalledXXX, package)]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.FailedToInstallXXX, package)]);
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }

                logger.LogError(
                    ex,
                    "An exception occurred during package installation. ({PackageId}/{Version})",
                    package.Id,
                    release?.Version?.Value ?? package.Version.ToString());
            }
            finally
            {
                if (File.Exists(nupkgPath))
                {
                    File.Delete(nupkgPath);
                }
            }
        }
    }
}
