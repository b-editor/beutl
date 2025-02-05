using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class UninstallViewModel(BeutlApiApplication app, ChangesModel changesModel, PackageChangeModel model)
    : ActionViewModel(changesModel, model), IProgress<double>
{
    private readonly ILogger _logger = Log.CreateLogger<UninstallViewModel>();
    private readonly ChangesModel _changesModel = changesModel;

    public ReactiveProperty<string> Message { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<double> Progress { get; } = new();

    public void Run(CancellationToken token)
    {
        try
        {
            _logger.LogInformation("Starting uninstallation process for package {PackageId} version {Version}.", Model.Id, Model.Version);

            string[]? installeds;
            var package = new PackageIdentity(Model.Id, Model.Version);
            InstalledPackageRepository repos = app.GetResource<InstalledPackageRepository>();
            PackageInstaller installer = app.GetResource<PackageInstaller>();

            if (!package.HasVersion)
            {
                installeds = repos.GetLocalPackages(package.Id)
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
                _logger.LogWarning("Package {PackageId} has already been uninstalled.", package.Id);
                repos.RemovePackages(package.Id);
                Message.Value = Strings.This_package_has_already_been_uninstalled;
            }
            else
            {
                foreach (string installed in installeds)
                {
                    _logger.LogInformation("Preparing to uninstall package from path {Path}.", installed);
                    PackageUninstallContext context = installer.PrepareForUninstall(installed, true, token);

                    Message.Value = Strings.Deleting_files;
                    installer.Uninstall(context, this, token);

                    _logger.LogInformation("Successfully uninstalled package {PackageId}.", context.Id);
                    Message.Value = string.Format(Strings.Uninstalled_XXX, context.Id);
                    if (context.FailedPackages?.Count > 0)
                    {
                        _logger.LogError("Failed to delete some packages: {FailedPackages}.", string.Join(", ", context.FailedPackages));
                        ErrorMessage.Value = $"""
                            {Strings.These_packages_were_not_deleted_successfully}
                            {string.Join('\n', context.FailedPackages.Select(i => $"- {Path.GetFileName(i)}"))}
                            """;
                    }
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

    void IProgress<double>.Report(double value)
    {
        if (double.IsFinite(value))
        {
            Progress.Value = value * 100;
        }
    }
}
