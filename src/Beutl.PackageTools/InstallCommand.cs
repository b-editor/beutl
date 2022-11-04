using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class InstallCommand : Command
{
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
                await commands.InstallPackages(installItems, verbose);
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
    public async Task InstallPackages(HashSet<(PackageIdentity, Release?)> items, bool verbose)
    {
        foreach ((PackageIdentity package, Release? release) in items)
        {
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
                        await _installer.DownloadPackageFile(context, null, _cancellationToken);
                    });
                    nupkgPath = context.NuGetPackageFile;
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

                Console.WriteLine(Chalk.BrightGreen[string.Format(Resources.InstalledXXX, package)]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(Chalk.BrightRed[string.Format(Resources.FailedToInstallXXX, package)]);
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }
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
