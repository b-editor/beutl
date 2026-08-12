using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.VersionControl;
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

    private readonly EditorService _editorService;
    private readonly ProjectService _projectService;

    public PackageOperationHandler(BeutlApiApplication app, EditorService editorService, ProjectService projectService)
    {
        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();
        _packageManager = app.GetResource<PackageManager>();
        _packageInstaller = app.GetResource<PackageInstaller>();
        _editorService = editorService;
        _projectService = projectService;
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

        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId)
                           ?? throw new InvalidOperationException(
                               $"Package '{packageId}' was not found under the install directory after installation.");
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

        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId)
                           ?? throw new InvalidOperationException(
                               $"Package '{packageId}' was not found under the install directory after installation.");
        PackageFolderReader reader = new(directory);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = directory };
        _packageManager.Load(localPackage);
    }

    public async ValueTask<bool> UnloadPackages(string packageName)
    {
        bool result = true;
        foreach (LocalPackage pkg in _packageManager.FindLoadedPackage(packageName))
        {
            result &= await _packageManager.Unload(pkg);
        }

        GC.Collect();
        GC.WaitForFullGCComplete(-1);
        GC.WaitForPendingFinalizers();

        return result;
    }

    public void DeleteOldVersionFiles(string packageName)
    {
        foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(packageName))
        {
            string directory = Helper.ResolveInstalledDirectory(item);
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
                string directory = Helper.ResolveInstalledDirectory(item);
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

    public async Task<bool> EnsureProjectClosed()
    {
        if (!_projectService.IsOpened.Value)
            return true;

        var dialog = new ContentDialog
        {
            Title = ExtensionsStrings.PackageInstaller,
            Content = ExtensionsStrings.PackageInstaller_CloseProjectConfirmation,
            PrimaryButtonText = Strings.OK,
            SecondaryButtonText = ExtensionsStrings.PackageInstaller_SaveAndClose,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Secondary
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Secondary)
        {
            // "Save and close" must not close on a failed save: closing would discard exactly the
            // edits that could not be written.
            if (!await SaveAll())
            {
                return false;
            }

            await _projectService.CloseProject();
            return true;
        }

        if (result == ContentDialogResult.Primary)
        {
            await _projectService.CloseProject();
            return true;
        }

        return false;
    }

    private async Task<bool> SaveAll()
    {
        using IProjectFileWriteLease fileWrite = await _editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);
        if (_projectService.CurrentProject.Value is { } project)
        {
            CoreSerializer.StoreToUri(project, project.Uri!);
        }

        // Every editor is saved independently rather than through SaveProjectFilesAsync: the caller
        // closes the project right after this, so one editor that cannot save must not discard the
        // edits of the ones that follow it.
        bool allSaved = true;
        foreach (EditorTabItem item in _editorService.TabItems.ToArray())
        {
            try
            {
                if (item.Commands.Value is { } commands && !await commands.OnSave())
                {
                    allSaved = false;
                    s_logger.LogWarning(
                        "{FileName} could not be saved before the package operation closed the project.",
                        item.FileName.Value);
                }
            }
            catch (Exception ex)
            {
                allSaved = false;
                s_logger.LogError(
                    ex,
                    "{FileName} threw while being saved before the package operation closed the project.",
                    item.FileName.Value);
            }
        }

        if (!allSaved)
        {
            NotificationService.ShowError(
                ExtensionsStrings.PackageInstaller,
                MessageStrings.UnableToSaveFile);
        }

        return allSaved;
    }

    public void Cancel(string packageName)
    {
        _queue.Cancel(packageName);
    }
}
