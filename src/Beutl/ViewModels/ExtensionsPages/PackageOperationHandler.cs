using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.ViewModels.ExtensionsPages;

internal class PackageOperationHandler
{
    private static readonly ILogger s_logger = Log.CreateLogger<PackageOperationHandler>();

    private readonly BeutlApiApplication _app;
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly PackageManager _packageManager;
    private readonly PackageInstaller _packageInstaller;

    private readonly EditorService _editorService;
    private readonly ProjectService _projectService;

    public PackageOperationHandler(BeutlApiApplication app, EditorService editorService, ProjectService projectService)
    {
        _app = app;
        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();
        _packageManager = app.GetResource<PackageManager>();
        _packageInstaller = app.GetResource<PackageInstaller>();
        _editorService = editorService;
        _projectService = projectService;
    }

    public InstalledPackageRepository InstalledPackageRepository => _installedPackageRepository;

    public PackageChangesQueue Queue => _queue;

    public async Task DownloadAndLoadPackage(
        Release release,
        PackageIdentity packageId,
        CancellationToken cancellationToken)
    {
        using var lifetime = _app.CreateLifetimeLinkedTokenSource(cancellationToken);
        cancellationToken = lifetime.Token;
        await _packageInstaller.TrackInstallOperationWithShutdownFallbackAsync(async () =>
        {
            PackageInstallContext context = await _packageInstaller.PrepareForInstall(
                release,
                force: true,
                cancellationToken).ConfigureAwait(false);
            await _packageInstaller.DownloadPackageFile(context, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _packageInstaller.VerifyPackageFile(context, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!context.HashVerified)
                throw new InvalidDataException("The package hash could not be verified.");
            await _packageInstaller.ResolveDependencies(context, null, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await ActivateInstalledPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        }, () => _queue.InstallQueue(packageId)).ConfigureAwait(false);
    }

    public async Task DownloadAndLoadPackage(
        PackageIdentity packageId,
        CancellationToken cancellationToken)
    {
        using var lifetime = _app.CreateLifetimeLinkedTokenSource(cancellationToken);
        cancellationToken = lifetime.Token;
        await _packageInstaller.TrackInstallOperationWithShutdownFallbackAsync(async () =>
        {
            PackageInstallContext context = _packageInstaller.PrepareForInstall(
                packageId.Id,
                packageId.Version.ToString(),
                force: true,
                cancellationToken);
            await _packageInstaller.DownloadPackageFile(context, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _packageInstaller.VerifyPackageFile(context, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!context.HashVerified)
                throw new InvalidDataException("The package hash could not be verified.");
            await _packageInstaller.ResolveDependencies(context, null, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await ActivateInstalledPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        }, () => _queue.InstallQueue(packageId)).ConfigureAwait(false);
    }

    private async Task ActivateInstalledPackageAsync(PackageIdentity packageId, CancellationToken cancellationToken)
    {
        string directory = Helper.PackagePathResolver.GetInstalledPath(packageId)
                           ?? throw new InvalidOperationException(
                               $"Package '{packageId}' was not found under the install directory after installation.");
        using PackageFolderReader reader = new(directory);
        var localPackage = new LocalPackage(reader.NuspecReader) { InstalledPath = directory };

        bool isExtension = localPackage.Tags.GetPackageKind() == PackageKind.Extension;
        var deployment = await Task.Run(() => _packageInstaller.PrepareDataPackage(localPackage, cancellationToken), cancellationToken).ConfigureAwait(false);
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool extensionLoaded = false;
                try
                {
                    // Extension hooks and repository observers run outside the payload gate.
                    // A failed load leaves the old payload untouched; failed publication or
                    // persistence unloads this newly loaded extension in the catch below.
                    if (isExtension)
                    {
                        _packageManager.Load(localPackage);
                        extensionLoaded = true;
                    }
                    Action? notify = null;
                    deployment.Commit(() => notify = _installedPackageRepository.UpgradePackagesAndDeferNotifications(packageId));
                    notify!();
                }
                catch (Exception failure)
                {
                    if (extensionLoaded)
                    {
                        try
                        {
                            if (!await _packageManager.Unload(localPackage))
                                throw new InvalidOperationException("The unregistered extension could not be unloaded.");
                        }
                        catch (Exception cleanup)
                        {
                            throw new AggregateException("Package registration and extension cleanup failed.", failure, cleanup);
                        }
                    }
                    throw;
                }
            }, Avalonia.Threading.DispatcherPriority.Default, cancellationToken).GetTask().Unwrap().ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(deployment.Dispose).ConfigureAwait(false);
        }
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
                else
                {
                    // The extracted package is already gone, but a data package's payload
                    // lives outside it and still has to be removed.
                    if (!_packageInstaller.UninstallDataPackage(item.Id))
                    {
                        _queue.UninstallQueue(item);
                        hasFallback = true;
                    }
                    else
                    {
                        _installedPackageRepository.RemovePackage(item);
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
        Project? expectedProject = _projectService.CurrentProject.Value;
        if (expectedProject is null)
            return true;

        var dialog = new FAContentDialog
        {
            Title = ExtensionsStrings.PackageInstaller,
            Content = ExtensionsStrings.PackageInstaller_CloseProjectConfirmation,
            PrimaryButtonText = Strings.OK,
            SecondaryButtonText = ExtensionsStrings.PackageInstaller_SaveAndClose,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Secondary
        };

        return await HandleProjectCloseChoice(await dialog.ShowAsync(), expectedProject);
    }

    internal async Task<bool> HandleProjectCloseChoice(
        FAContentDialogResult result,
        Project expectedProject)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        if (result == FAContentDialogResult.Secondary)
        {
            IProjectFileWriteLease? fileWrite = null;
            IDisposable? editorSuspension = null;
            try
            {
                fileWrite = await _editorService.BeginProjectFileWriteAsync(CancellationToken.None);
                if (!ReferenceEquals(
                        _projectService.CurrentProject.Value,
                        expectedProject))
                {
                    return false;
                }

                editorSuspension = _editorService.SuspendEditors();
                if (!await SaveAll(expectedProject, fileWrite))
                {
                    return false;
                }

                fileWrite.Dispose();
                fileWrite = null;
                return await _projectService.TryCloseProjectAsync(
                    expectedProject,
                    ProjectService.ProjectCloseIntent.SaveChanges);
            }
            finally
            {
                fileWrite?.Dispose();
                editorSuspension?.Dispose();
            }
        }

        if (result == FAContentDialogResult.Primary)
        {
            return await _projectService.TryCloseProjectAsync(
                expectedProject,
                ProjectService.ProjectCloseIntent.DiscardChanges);
        }

        return false;
    }

    private async Task<bool> SaveAll(Project project, IProjectFileWriteLease fileWrite)
    {
        try
        {
            IProjectVersionControlSession? versionControlSession
                = _editorService.ProjectVersionControlSession;
            if (!await _editorService.SaveProjectFilesAsync(project, CancellationToken.None))
            {
                return false;
            }

            if (!ReferenceEquals(_projectService.CurrentProject.Value, project)
                || !ReferenceEquals(
                    _editorService.ProjectVersionControlSession,
                    versionControlSession))
            {
                return false;
            }

            if (versionControlSession is not null)
            {
                await versionControlSession.NotifySavedAsync(fileWrite);
            }

            return ReferenceEquals(_projectService.CurrentProject.Value, project)
                   && ReferenceEquals(
                       _editorService.ProjectVersionControlSession,
                       versionControlSession);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            s_logger.LogError(ex, "Failed to save the project before a package operation.");
            return false;
        }
    }

    public void Cancel(string packageName)
    {
        _queue.Cancel(packageName);
    }
}
