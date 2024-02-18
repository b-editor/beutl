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
            _logger.LogInformation(
                "Uninstall the package. ({PackageId}/{Version})",
                Model.Id, Model.Version);

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
                repos.RemovePackages(package.Id);
                Message.Value = Strings.This_package_has_already_been_uninstalled;
            }
            else
            {
                foreach (string installed in installeds)
                {
                    PackageUninstallContext context = installer.PrepareForUninstall(installed, true, token);

                    Message.Value = Strings.Deleting_files;
                    installer.Uninstall(context, this, token);

                    Message.Value = string.Format(Strings.Uninstalled_XXX, context.Id);
                    if (context.FailedPackages?.Count > 0)
                    {
                        ErrorMessage.Value = $"""
                            {Strings.These_packages_were_not_deleted_successfully}
                            {string.Join('\n', context.FailedPackages.Select(i => $"- {Path.GetFileName(i)}"))}
                            """;
                    }
                }
            }

            Succeeded.Value = true;
            return;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage.Value = Strings.Operation_canceled;
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occured.");
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
