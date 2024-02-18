using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class InstallViewModel(BeutlApiApplication app, ChangesModel changesModel, PackageChangeModel model)
    : ActionViewModel(changesModel, model)
{
    private readonly ILogger _logger = Log.CreateLogger<InstallViewModel>();
    private readonly ChangesModel _changesModel = changesModel;

    public ReactiveProperty<DownloadTaskModel> Download { get; } = new();

    public ReactiveProperty<VerifyTaskModel> Verify { get; } = new();

    public ReactiveProperty<ResolveTaskModel> Resolve { get; } = new();

    public ReactiveProperty<AcceptLicenseTaskModel> AcceptLicense { get; } = new();

    public ReactiveProperty<object?> CurrentRunningTask { get; } = new();

    public async Task Run(CancellationToken token)
    {
        try
        {
            _logger.LogInformation(
                "Install the package. ({PackageId}/{Version})",
                Model.Id, Model.Version.ToString());

            CurrentRunningTask.Value = Download.Value = new DownloadTaskModel(Model, app);
            Download.Value.ShowDetails.Value = true;
            bool result = await Download.Value.Run(token);
            if (!result || Download.Value.Context == null)
                goto Failed;

            CurrentRunningTask.Value = Verify.Value = new VerifyTaskModel(app, Download.Value.Context);
            Verify.Value.ShowDetails.Value = true;
            result = await Verify.Value.Run(token);
            if (!result) goto Failed;

            CurrentRunningTask.Value = Resolve.Value = new ResolveTaskModel(app, Download.Value.Context);
            Resolve.Value.ShowDetails.Value = true;
            result = await Resolve.Value.Run(token);
            if (!result) goto Failed;

            CurrentRunningTask.Value = AcceptLicense.Value = new AcceptLicenseTaskModel(app, Download.Value.Context);
            AcceptLicense.Value.ShowDetails.Value = true;
            result = await AcceptLicense.Value.Run(token);
            if (!result) goto Failed;

            // インストールされているパッケージのリストに追加
            InstalledPackageRepository repos = app.GetResource<InstalledPackageRepository>();
            var pkg = new PackageIdentity(Model.Id, Model.Version);
            repos.AddPackage(pkg);
            repos.UpgradePackages(pkg);
            Succeeded.Value = true;
            return;

        Failed:
            Failed.Value = true;
        }
        catch (OperationCanceledException)
        {
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occured.");
            Failed.Value = true;
        }
        finally
        {
            CurrentRunningTask.Value = null;
            string? file = Download.Value?.Context?.NuGetPackageFile;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
