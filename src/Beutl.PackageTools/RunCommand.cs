using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class RunCommand : RootCommand
{
    private readonly Option<string[]> _installs;
    private readonly Option<string[]> _uninstalls;
    private readonly Option<string[]> _updates;
    private readonly Option<bool> _verbose;
    private readonly Option<bool> _clean;
    private readonly BeutlApiApplication _apiApp;

    public RunCommand(BeutlApiApplication apiApp, Option<bool> verbose, Option<bool> clean)
        : base(Resources.RunCommandDescription)
    {
        AddOption(_installs = new Option<string[]>(["--installs", "-i"], () => [])
        {
            Description = Resources.InstallsDescription,
            AllowMultipleArgumentsPerToken = true,
        });
        AddOption(_uninstalls = new Option<string[]>(["--uninstalls", "-r"], () => [])
        {
            Description = Resources.UninstallsDescription,
            AllowMultipleArgumentsPerToken = true,
        });
        AddOption(_updates = new Option<string[]>(["--updates", "-u"], () => [])
        {
            Description = Resources.UpdatesDescription,
            AllowMultipleArgumentsPerToken = true,
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

            string[] installs = context.ParseResult.GetValueForOption(_installs)!;
            string[] uninstalls = context.ParseResult.GetValueForOption(_uninstalls)!;
            string[] updates = context.ParseResult.GetValueForOption(_updates)!;
            bool verbose = context.ParseResult.GetValueForOption(_verbose);
            bool clean = context.ParseResult.GetValueForOption(_clean);

            var installItems = new HashSet<(PackageIdentity, Release?)>();
            var uninstallItems = new HashSet<(PackageIdentity, Release?)>();
            var updateItems = new HashSet<(PackageIdentity, Release?)>();
            await parser.LoadArgs(installItems, installs, true, cancellationToken);
            await parser.LoadArgs(uninstallItems, uninstalls, false, cancellationToken);
            await parser.LoadArgs(updateItems, updates, true, cancellationToken);

            PackageDisplay.Show(installItems, updateItems, uninstallItems);

            Console.WriteLine(Resources.PerformTheAboveOperations);
            if (!Prompt.Confirm(Resources.AreYouReady, defaultValue: true))
            {
                return;
            }
            else
            {
                await commands.InstallPackages(installItems, verbose);
                await commands.UpdatePackages(updateItems, verbose);
                commands.UninstallPackages(uninstallItems, verbose, clean);
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
