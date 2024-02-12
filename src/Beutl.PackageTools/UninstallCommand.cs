using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.Logging;
using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class UninstallCommand : Command
{
    private readonly ILogger _logger = Log.CreateLogger<UninstallCommand>();
    private readonly Argument<string[]> _uninstalls;
    private readonly Option<bool> _verbose;
    private readonly Option<bool> _clean;
    private readonly BeutlApiApplication _apiApp;

    public UninstallCommand(BeutlApiApplication apiApp, Option<bool> verbose, Option<bool> clean)
        : base("uninstall", Resources.UninstallCommandDescription)
    {
        AddArgument(_uninstalls = new Argument<string[]>(() => Array.Empty<string>())
        {
            Description = Resources.UninstallsDescription,
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

            string[] uninstalls = context.ParseResult.GetValueForArgument(_uninstalls)!;
            bool verbose = context.ParseResult.GetValueForOption(_verbose);
            bool clean = context.ParseResult.GetValueForOption(_clean);

            var uninstallItems = new HashSet<(PackageIdentity, Release?)>();
            await parser.LoadArgs(uninstallItems, uninstalls, false, cancellationToken);

            PackageDisplay.Show(null, null, uninstallItems);

            Console.WriteLine(Resources.PerformTheAboveOperations);
            if (!Prompt.Confirm(Resources.AreYouReady, defaultValue: true))
            {
                return;
            }
            else
            {
                commands.UninstallPackages(uninstallItems, verbose, clean, _logger);
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
    public void UninstallPackages(HashSet<(PackageIdentity, Release?)> items, bool verbose, bool clean, ILogger logger)
    {
        foreach ((PackageIdentity package, Release? release) in items)
        {
            Console.WriteLine();
            try
            {
                string[]? installeds;

                if (!package.HasVersion)
                {
                    installeds = _installedPackageRepository.GetLocalPackages(package.Id)
                        .Select(x => Helper.PackagePathResolver.GetInstalledPath(x))
                        .Where(x => x != null)
                        .ToArray();
                }
                else
                {
                    string installed = Helper.PackagePathResolver.GetInstalledPath(package);
                    if (installed != null)
                    {
                        installeds = [installed];
                    }
                    else
                    {
                        installeds = [];
                    }
                }

                if (installeds.Length <= 0)
                {
                    _installedPackageRepository.RemovePackages(package.Id);
                    Console.WriteLine(Resources.ThisPackageHasAlreadyBeenUninstalled);
                }
                else
                {
                    foreach (string installed in installeds)
                    {
                        logger.LogInformation(
                            "Uninstall the package. ({PackageId}/{Version})",
                            package.Id,
                            release?.Version?.Value ?? package.Version.ToString());

                        PackageUninstallContext context = _installer.PrepareForUninstall(installed, clean, _cancellationToken);

                        Console.WriteLine(Resources.UninstallingXXX, context.Id);

                        string message = Resources.DeletingFiles;
                        Spinner.Start(message, spinner =>
                        {
                            var progress = new KurukuruProgress(spinner, message);
                            _installer.Uninstall(context, progress, _cancellationToken);
                        });

                        Console.WriteLine(Resources.UninstalledXXX, context.Id);
                        if (context.FailedPackages?.Count > 0)
                        {
                            foreach (string item in context.FailedPackages)
                            {
                                Console.Error.WriteLine(Chalk.BrightRed[Path.GetFileName(item)]);
                            }

                            Console.WriteLine(Chalk.BrightRed[Resources.ThesePackagesWereNotDeletedSuccessfully]);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.FailedToUninstallXXX, package)]);
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }

                logger.LogError(
                    ex,
                    "An exception occurred during package uninstallation. ({PackageId}/{Version})",
                    package.Id,
                    release?.Version?.Value ?? package.Version.ToString());
            }
        }
    }
}
