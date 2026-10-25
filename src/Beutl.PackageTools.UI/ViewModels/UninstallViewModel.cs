using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class UninstallViewModel(BeutlApiApplication app, ChangesModel changesModel, PackageChangeModel model)
    : ActionViewModel(changesModel, model)
{
    private readonly ILogger _logger = Log.CreateLogger<UninstallViewModel>();

    public ReactiveProperty<string> Message { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ProgressState Progress { get; } = new();

    public void Run(CancellationToken token)
    {
        try
        {
            _logger.LogInformation("Starting uninstallation process for package {PackageId} version {Version}.", Model.Id, Model.Version);

            var package = new PackageIdentity(Model.Id, Model.Version);
            InstalledPackageRepository repos = app.GetResource<InstalledPackageRepository>();
            PackageInstaller installer = app.GetResource<PackageInstaller>();

            if (repos.RestoreFailed)
            {
                ErrorMessage.Value = Strings.Installed_package_registry_could_not_be_restored;
                Failed.Value = true;
                return;
            }

            string[]? installeds = ResolveInstalledDirectories(package);

            if (installeds.Length <= 0)
            {
                _logger.LogWarning("Package {PackageId} has already been uninstalled.", package.Id);
                // A data package's payload lives outside the install directory, so try to remove it even when
                // the extracted files are gone. Keep the repository entry until the removal succeeds.
                if (installer.UninstallDataPackage(package.Id))
                {
                    repos.RemovePackages(package.Id);
                    Message.Value = Strings.This_package_has_already_been_uninstalled;
                }
                else
                {
                    _logger.LogError("Failed to delete the data payload of {PackageId}.", package.Id);
                    ErrorMessage.Value = $"""
                        {Strings.These_packages_were_not_deleted_successfully}
                        - {package.Id}
                        """;
                    // Falling through would report success and drop the queue, orphaning
                    // a payload that is still installed.
                    Failed.Value = true;
                    return;
                }
            }
            else
            {
                foreach (string installed in installeds)
                {
                    _logger.LogInformation("Preparing to uninstall package from path {Path}.", installed);
                    PackageUninstallContext context = installer.PrepareForUninstall(installed, true, token);

                    Message.Value = Strings.Deleting_files;
                    installer.Uninstall(context, Progress, token);

                    if (context.FailedPackages?.Count > 0)
                    {
                        _logger.LogError("Failed to delete some packages: {FailedPackages}.", string.Join(", ", context.FailedPackages));
                        ErrorMessage.Value = $"""
                            {Strings.These_packages_were_not_deleted_successfully}
                            {string.Join('\n', context.FailedPackages.Select(i => $"- {Path.GetFileName(i)}"))}
                            """;
                        // Reporting success here would drop the queue while a payload or
                        // extracted file is still on disk.
                        Failed.Value = true;
                        return;
                    }

                    _logger.LogInformation("Successfully uninstalled package {PackageId}.", context.Id);
                    Message.Value = string.Format(Strings.Uninstalled_XXX, context.Id);
                }
            }

            _logger.LogInformation("Uninstallation process completed successfully for package {PackageId}.", Model.Id);
            Succeeded.Value = true;
            return;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Uninstallation process for package {PackageId} was canceled.", Model.Id);
            ErrorMessage.Value = Strings.Operation_canceled;
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during the uninstallation process for package {PackageId}.", Model.Id);
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
        }
    }

    private static string[] ResolveInstalledDirectories(PackageIdentity package)
    {
        string installed = Helper.ResolveInstalledDirectory(package);
        return Directory.Exists(installed) ? [installed] : [];
    }
}
