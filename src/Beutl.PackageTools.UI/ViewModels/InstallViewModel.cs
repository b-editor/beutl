using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

using NuGet.Packaging;
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
                "Starting installation of package {PackageId} version {Version}.",
                Model.Id, Model.Version.ToString());

            CurrentRunningTask.Value = Download.Value = new DownloadTaskModel(Model, app);
            Download.Value.ShowDetails.Value = true;
            _logger.LogInformation("Downloading package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            bool result = await Download.Value.Run(token);
            if (!result || Download.Value.Context == null)
            {
                _logger.LogWarning("Download failed for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
                goto Failed;
            }

            CurrentRunningTask.Value = Verify.Value = new VerifyTaskModel(app, Download.Value.Context);
            Verify.Value.ShowDetails.Value = true;
            _logger.LogInformation("Verifying package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            result = await Verify.Value.Run(token);
            if (!result)
            {
                _logger.LogWarning("Verification failed for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
                goto Failed;
            }

            CurrentRunningTask.Value = Resolve.Value = new ResolveTaskModel(app, Download.Value.Context);
            Resolve.Value.ShowDetails.Value = true;
            _logger.LogInformation("Resolving dependencies for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            result = await Resolve.Value.Run(token);
            if (!result)
            {
                _logger.LogWarning("Dependency resolution failed for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
                goto Failed;
            }

            CurrentRunningTask.Value = AcceptLicense.Value = new AcceptLicenseTaskModel(app, Download.Value.Context);
            AcceptLicense.Value.ShowDetails.Value = true;
            _logger.LogInformation("Accepting license for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            result = await AcceptLicense.Value.Run(token);
            if (!result)
            {
                _logger.LogWarning("License acceptance failed for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
                goto Failed;
            }

            // インストールされているパッケージのリストに追加
            InstalledPackageRepository repos = app.GetResource<InstalledPackageRepository>();
            var pkg = new PackageIdentity(Model.Id, Model.Version);

            var deployment = await Task.Run(() => PrepareDataPackage(pkg, token), token);
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    // Cancellation is no longer accepted once publication starts. A failed
                    // registration still restores the previous payload as part of Commit.
                    deployment.Commit(() => repos.UpgradePackages(pkg));
                    Succeeded.Value = true;
                }, Avalonia.Threading.DispatcherPriority.Default, token);
            }
            finally
            {
                await Task.Run(deployment.Dispose);
            }
            _logger.LogInformation("Package {PackageId} version {Version} installed successfully.", Model.Id, Model.Version.ToString());
            return;

        Failed:
            _logger.LogError("Installation failed for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            Failed.Value = true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Installation canceled for package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            Canceled.Value = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during installation of package {PackageId} version {Version}.", Model.Id, Model.Version.ToString());
            Failed.Value = true;
        }
        finally
        {
            CurrentRunningTask.Value = null;
            string? file = Download.Value?.Context?.NuGetPackageFile;
            if (File.Exists(file))
            {
                File.Delete(file);
                _logger.LogInformation("Deleted temporary file {File}.", file);
            }
        }
    }

    private PackageInstaller.DataPackageDeployment PrepareDataPackage(PackageIdentity pkg, CancellationToken token)
    {
        string? directory = Helper.PackagePathResolver.GetInstalledPath(pkg);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Installed directory not found for package '{pkg.Id}'.");
        using var reader = new PackageFolderReader(directory);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = directory };
        return app.GetResource<PackageInstaller>().PrepareDataPackage(localPackage, token);
    }
}
