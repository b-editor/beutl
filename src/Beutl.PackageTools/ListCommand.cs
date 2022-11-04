using System.CodeDom.Compiler;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

using Beutl.PackageTools.Properties;

using FluentTextTable;

using NuGet.Frameworks;
using NuGet.Packaging;

namespace Beutl.PackageTools;

public sealed class ListCommand : Command
{
    private readonly Option<bool> _installed;
    private readonly Option<bool> _update;
    private readonly Option<bool> _unnecessary;
    private readonly Option<bool> _verbose;
    private readonly BeutlApiApplication _apiApp;

    public ListCommand(BeutlApiApplication apiApp, Option<bool> verbose)
        : base("list", Resources.ListCommandDescription)
    {
        AddOption(_installed = new Option<bool>("--installed", () => true));
        AddOption(_update = new Option<bool>("--update", () => true));
        AddOption(_unnecessary = new Option<bool>("--unnecessary", () => true));

        _apiApp = apiApp;
        _verbose = verbose;
        this.SetHandler(InvokeAsync);
    }

    private async Task InvokeAsync(InvocationContext context)
    {
        try
        {
            CancellationToken cancellationToken = context.GetCancellationToken();

            OptionResult? installedResult = context.ParseResult.FindResultFor(_installed);
            OptionResult? updateResult = context.ParseResult.FindResultFor(_update);
            OptionResult? unnecessaryResult = context.ParseResult.FindResultFor(_unnecessary);
            bool verbose = context.ParseResult.GetValueForOption(_verbose);
            bool installed = true;
            bool update = true;
            bool unnecessary = true;

            // どれか一つでも明示している場合それに従う。
            if (installedResult?.IsImplicit == false
                || updateResult?.IsImplicit == false
                || unnecessaryResult?.IsImplicit == false)
            {
                installed = installedResult?.GetValueOrDefault<bool>() ?? false;
                update = updateResult?.GetValueOrDefault<bool>() ?? false;
                unnecessary = unnecessaryResult?.GetValueOrDefault<bool>() ?? false;
            }

            InstalledPackageRepository installedPackageRepository = _apiApp.GetResource<InstalledPackageRepository>();
            PackageManager manager = _apiApp.GetResource<PackageManager>();
            PackageInstaller installer = _apiApp.GetResource<PackageInstaller>();
            if (installed)
            {
                bool any = false;
                using (var writer = new IndentedTextWriter(Console.Out, "  "))
                {
                    writer.WriteLine(Resources.Installed);
                    writer.Indent++;
                    foreach (PackageIdentity item in installedPackageRepository.GetLocalPackages())
                    {
                        any = true;
                        if (verbose)
                        {
                            VerboseInstalledOutput(item, writer);
                        }
                        else
                        {
                            writer.WriteLine(item);
                        }
                    }

                    if (!any)
                    {
                        writer.WriteLine(Resources.Nothing);
                    }
                    writer.Indent--;
                }
            }

            if (update)
            {
                Console.WriteLine();
                IReadOnlyList<PackageUpdate> updates = await Spinner.StartAsync(Resources.CheckingForUpdates, async () =>
                {
                    return await manager.CheckUpdate();
                });

                Console.WriteLine(Resources.UpdateAvailable);

                IEnumerable<UpdateModel> items = updates.Select(item => new UpdateModel(
                    item.Package.Owner.Name,
                    item.Package.Name,
                    item.OldVersion?.Version.Value ?? Resources.Unknown,
                    item.NewVersion.Version.Value));
                Build
                    .TextTable<UpdateModel>(builder =>
                        builder.Columns.Add(x => x.Publisher).NameAs(Resources.Publisher)
                            .Columns.Add(x => x.Name).NameAs(Resources.Name)
                            .Columns.Add(x => x.CurrentVersion).NameAs(Resources.CurrentVersion)
                            .Columns.Add(x => x.NewVersion).NameAs(Resources.NewVersion))
                    .WriteLine(items);
            }

            if (unnecessary)
            {
                Console.WriteLine();
                PackageCleanContext cleanContext = installer.PrepareForClean();

                Console.WriteLine(Resources.UnnecessaryPackages);
                foreach (PackageIdentity item in cleanContext.UnnecessaryPackages)
                {
                    Console.WriteLine($"  {item}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(Resources.OperationCanceled);
        }
    }

    private void VerboseInstalledOutput(PackageIdentity packageId, IndentedTextWriter textWriter)
    {
        NuGetFramework framework = Helper.GetFrameworkName();

        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
        if (directory != null)
        {
            var reader = new PackageFolderReader(directory);

            IEnumerable<PackageDependencyGroup> deps = reader.GetPackageDependencies();
            NuGetFramework nearest = Helper.FrameworkReducer.GetNearest(
                framework,
                deps.Select(x => x.TargetFramework));

            VerboseInstalledOutputCore(
                new PackageDependencyInfo(packageId, deps
                    .Where(x => x.TargetFramework == nearest)
                    .SelectMany(x => x.Packages)),
                framework,
                textWriter);
        }
        else
        {
            textWriter.WriteLine($"{packageId} [{Resources.NotFound}]");
        }
    }

    private void VerboseInstalledOutputCore(
        PackageDependencyInfo package,
        NuGetFramework framework,
        IndentedTextWriter textWriter)
    {
        try
        {
            textWriter.WriteLine(package.HasVersion ? $"{package.Id}.{package.Version}" : package.Id);
            textWriter.Indent++;

            foreach (PackageDependency? dependency in package.Dependencies)
            {
                var dependentPackage = new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion);
                string directory = Helper.PackagePathResolver.GetInstalledPath(dependentPackage);
                if (directory != null)
                {
                    var reader = new PackageFolderReader(directory);

                    IEnumerable<PackageDependencyGroup> deps = reader.GetPackageDependencies();
                    NuGetFramework nearest = Helper.FrameworkReducer.GetNearest(
                        framework,
                        deps.Select(x => x.TargetFramework));

                    VerboseInstalledOutputCore(
                        new PackageDependencyInfo(dependentPackage, deps
                            .Where(x => x.TargetFramework == nearest).SelectMany(x => x.Packages)),
                        framework,
                        textWriter);
                }
                else
                {
                    textWriter.WriteLine($"{dependentPackage} [{Resources.NotFound}]");
                }
            }
        }
        finally
        {
            textWriter.Indent--;
        }
    }

    private record UpdateModel(string Publisher, string Name, string CurrentVersion, string NewVersion);
}
