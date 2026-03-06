using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.ViewModels.ExtensionsPages;

internal class PackageOperationHandler
{
    private static readonly ILogger s_logger = Log.CreateLogger<PackageOperationHandler>();

    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly PackageManager _packageManager;
    private readonly PackageInstaller _packageInstaller;

    public PackageOperationHandler(BeutlApiApplication app)
    {
        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();
        _packageManager = app.GetResource<PackageManager>();
        _packageInstaller = app.GetResource<PackageInstaller>();
    }

    public InstalledPackageRepository InstalledPackageRepository => _installedPackageRepository;

    public PackageChangesQueue Queue => _queue;

    public async Task DownloadAndLoadPackage(Release release, PackageIdentity packageId)
    {
        PackageInstallContext context = await _packageInstaller.PrepareForInstall(release, force: true);
        await _packageInstaller.DownloadPackageFile(context);
        await _packageInstaller.VerifyPackageFile(context);
        await _packageInstaller.ResolveDependencies(context, null);

        _installedPackageRepository.UpgradePackages(packageId);

        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
        PackageFolderReader reader = new(directory);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = directory };
        _packageManager.Load(localPackage);
    }

    public async Task DownloadAndLoadPackage(PackageIdentity packageId)
    {
        PackageInstallContext context = _packageInstaller.PrepareForInstall(packageId.Id, packageId.Version.ToString(), force: true);
        await _packageInstaller.DownloadPackageFile(context);
        await _packageInstaller.VerifyPackageFile(context);
        await _packageInstaller.ResolveDependencies(context, null);

        _installedPackageRepository.UpgradePackages(packageId);

        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId);
        PackageFolderReader reader = new(directory);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = directory };
        _packageManager.Load(localPackage);
    }

    public bool UnloadPackages(string packageName)
    {
        bool result = true;
        foreach (LocalPackage pkg in _packageManager.FindLoadedPackage(packageName))
        {
            result &= _packageManager.Unload(pkg);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        return result;
    }

    public void DeleteOldVersionFiles(string packageName)
    {
        foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(packageName))
        {
            string directory = Helper.PackagePathResolver.GetInstalledPath(item);
            if (Directory.Exists(directory))
            {
                PackageUninstallContext ctx = _packageInstaller.PrepareForUninstall(directory);
                _packageInstaller.Uninstall(ctx, new Progress<double>());
            }
        }
    }

    public bool UninstallWithFallback(string packageName)
    {
        bool hasFallback = false;
        foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(packageName))
        {
            try
            {
                string directory = Helper.PackagePathResolver.GetInstalledPath(item);
                if (Directory.Exists(directory))
                {
                    var ctx = _packageInstaller.PrepareForUninstall(directory);
                    _packageInstaller.Uninstall(ctx, new Progress<double>());

                    if (ctx.FailedPackages is { Count: > 0 })
                    {
                        _queue.UninstallQueue(item);
                        hasFallback = true;
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Immediate uninstall failed for {PackageId}, falling back to queue.", item.Id);
                _queue.UninstallQueue(item);
                hasFallback = true;
            }
        }

        return !hasFallback;
    }

    public bool UninstallSinglePackage(string? installedPath, PackageIdentity packageIdentity)
    {
        if (installedPath == null)
            return true;

        var ctx = _packageInstaller.PrepareForUninstall(installedPath);
        _packageInstaller.Uninstall(ctx, new Progress<double>());

        if (ctx.FailedPackages is { Count: > 0 })
        {
            s_logger.LogWarning("Some files could not be deleted, falling back to queue.");
            _queue.UninstallQueue(packageIdentity);
            return false;
        }

        return true;
    }

    public void QueueUninstallAll(string packageName)
    {
        foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(packageName))
        {
            _queue.UninstallQueue(item);
        }
    }

    public static async Task<bool> EnsureProjectClosed()
    {
        if (!ProjectService.Current.IsOpened.Value)
            return true;

        var dialog = new ContentDialog
        {
            Title = ExtensionsPage.PackageInstaller,
            Content = ExtensionsPage.PackageInstaller_CloseProjectConfirmation,
            PrimaryButtonText = Strings.OK,
            SecondaryButtonText = ExtensionsPage.PackageInstaller_SaveAndClose,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Secondary
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Secondary)
        {
            await SaveAll();
            ProjectService.Current.CloseProject();
            return true;
        }

        if (result == ContentDialogResult.Primary)
        {
            ProjectService.Current.CloseProject();
            return true;
        }

        return false;
    }

    private static async Task SaveAll()
    {
        Project? project = ProjectService.Current.CurrentProject.Value;
        if (project != null)
        {
            CoreSerializer.StoreToUri(project, project.Uri!);
        }

        foreach (EditorTabItem item in EditorService.Current.TabItems)
        {
            if (item.Commands.Value != null)
            {
                await item.Commands.Value.OnSave();
            }
        }
    }

    public void Cancel(string packageName)
    {
        _queue.Cancel(packageName);
    }
}
