using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.Logging;
using Beutl.PackageTools.Properties;

using NuGet.Packaging;

namespace Beutl.PackageTools;

public sealed class UpdateCommand : Command
{
    private readonly ILogger _logger = Log.CreateLogger<UpdateCommand>();
    private readonly Argument<string[]> _updates;
    private readonly Option<bool> _verbose;
    private readonly Option<bool> _clean;
    private readonly BeutlApiApplication _apiApp;

    public UpdateCommand(BeutlApiApplication apiApp, Option<bool> verbose, Option<bool> clean)
        : base("update", Resources.UpdateCommandDescription)
    {
        AddArgument(_updates = new Argument<string[]>(() => Array.Empty<string>())
        {
            Description = Resources.UpdatesDescription,
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

            string[] updates = context.ParseResult.GetValueForArgument(_updates)!;
            bool verbose = context.ParseResult.GetValueForOption(_verbose);
            bool clean = context.ParseResult.GetValueForOption(_clean);

            var updateItems = new HashSet<(PackageIdentity, Release?)>();
            if (updates.Length > 0)
            {
                await parser.LoadArgs(updateItems, updates, true, cancellationToken);
            }
            else
            {
                PackageManager manager = _apiApp.GetResource<PackageManager>();

                IReadOnlyList<PackageUpdate> items = await Spinner.StartAsync(Resources.CheckingForUpdates, async () =>
                {
                    return await manager.CheckUpdate();
                });

                if (items.Count <= 0)
                {
                    Console.WriteLine(Resources.NoUpdatesFound);
                    return;
                }
                else
                {
                    Console.WriteLine(Resources.XXXUpdatesFound, items.Count);
                    IEnumerable<PackageUpdate> selectedItems = Prompt.MultiSelect(
                        message: Resources.SelectThePackagesToBeUpdated,
                        items: items,
                        minimum: 0,
                        textSelector: x => $"{x.Package.Name}.{x.OldVersion?.Version?.Value ?? Resources.Unknown} > {x.NewVersion.Version.Value}");

                    foreach (PackageUpdate item in selectedItems)
                    {
                        var nugetVersion = new NuGetVersion(item.NewVersion.Version.Value);
                        updateItems.Add((new PackageIdentity(item.Package.Name, nugetVersion), item.NewVersion));
                    }
                }
            }

            PackageDisplay.Show(null, updateItems, null);

            Console.WriteLine(Resources.PerformTheAboveOperations);
            if (!Prompt.Confirm(Resources.AreYouReady, defaultValue: true))
            {
                return;
            }
            else
            {
                await commands.UpdatePackages(updateItems, verbose, _logger);
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
    public async Task UpdatePackages(HashSet<(PackageIdentity, Release?)> items, bool verbose, ILogger logger)
    {
        foreach ((PackageIdentity package, Release? release) in items)
        {
            logger.LogInformation(
                "Update the package. ({PackageId}/{Version})",
                package.Id,
                release?.Version?.Value ?? package.Version.ToString());

            Console.WriteLine();
            string? nupkgPath = null;

            try
            {
                Console.WriteLine(Resources.UpdatingXXX, package.Id);

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

                _installedPackageRepository.UpgradePackages(package);

                Console.WriteLine(Chalk.BrightGreen[string.Format(Resources.UpdatedXXX, package.Id)]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.FailedToUpdateXXX, package.Id)]);
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }

                logger.LogError(
                    ex,
                    "An exception occurred while updating the package. ({PackageId}/{Version})",
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
