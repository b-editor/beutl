using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class UpdateCommand : Command
{
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
                await commands.UpdatePackages(updateItems, verbose);
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
    public async Task UpdatePackages(HashSet<(PackageIdentity, Release?)> items, bool verbose)
    {
        foreach ((PackageIdentity package, Release? release) in items)
        {
            Console.WriteLine();

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
            }
        }
    }
}
