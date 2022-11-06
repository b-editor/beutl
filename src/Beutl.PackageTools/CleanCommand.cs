using System.CommandLine;
using System.CommandLine.Invocation;

using Beutl.PackageTools.Properties;

namespace Beutl.PackageTools;

public sealed class CleanCommand : Command
{
    private readonly BeutlApiApplication _apiApp;

    public CleanCommand(BeutlApiApplication apiApp)
        : base("clean", Resources.CleanCommandDescription)
    {
        _apiApp = apiApp;
        this.SetHandler(InvokeAsync);
    }

    private async Task InvokeAsync(InvocationContext context)
    {
        try
        {
            CancellationToken cancellationToken = context.GetCancellationToken();
            await WaitForProcessExited.Guard(cancellationToken);
            var commands = new InstallerCommands(_apiApp, cancellationToken);

            commands.CleanPackages();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(Resources.OperationCanceled);
        }
    }
}

public partial class InstallerCommands
{
    public void CleanPackages()
    {
        PackageCleanContext context = _installer.PrepareForClean(null, _cancellationToken);
        if (context.UnnecessaryPackages.Length <= 0)
        {
            return;
        }

        Console.WriteLine($"\n{Resources.DeleteUnnecessaryPackages}");

        IEnumerable<PackageIdentity> remove = Prompt.MultiSelect(
            Resources.SelectThePackagesToBeDeleted,
            context.UnnecessaryPackages,
            defaultValues: context.UnnecessaryPackages,
            textSelector: x => x.ToString(),
            minimum: 0);
        context = _installer.PrepareForClean(context.UnnecessaryPackages.Except(remove, PackageIdentityComparer.Default), _cancellationToken);

        Console.WriteLine();
        Console.WriteLine(string.Format(
            Resources.SomePackagesWillBeDeletedAndSomeDiskWillBeReleased,
            context.UnnecessaryPackages.Length,
            StringFormats.ToHumanReadableSize(context.SizeToBeReleased)));

        if (Prompt.Confirm(Resources.AreYouReady, true))
        {
            string message = Resources.DeletingUnnecessaryPackages;
            Spinner.Start(message, spinner =>
            {
                var progress = new KurukuruProgress(spinner, message);
                _installer.Clean(context, progress, _cancellationToken);
            });

            Console.WriteLine(Resources.DeletedUnnecessaryPackages);
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
