using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Reactive.Bindings;
using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class RemoteUserPackageViewModel : BaseViewModel, IUserPackageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<RemoteUserPackageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly PackageOperationHandler _handler;
    private readonly BeutlApiApplication _app;
    private readonly LibraryService _library;
    private readonly DiscoverService _discover;

    public RemoteUserPackageViewModel(Package package, BeutlApiApplication app)
    {
        Package = package;
        _app = app;
        _handler = new PackageOperationHandler(app);
        _library = app.GetResource<LibraryService>();
        _discover = app.GetResource<DiscoverService>();

        IObservable<PackageChangesQueue.EventType> observable = _handler.Queue.GetObservable(package.Name);
        CanCancel = observable.Select(x => x != PackageChangesQueue.EventType.None)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<bool> installed = _handler.InstalledPackageRepository.GetObservable(package.Name);
        IsInstallButtonVisible = installed
            .AnyTrue(CanCancel)
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUpdateButtonVisible = LatestRelease.Select(x => x != null)
            .AreTrue(CanCancel.Not())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUninstallButtonVisible = installed
            .AreTrue(CanCancel.Not(), IsUpdateButtonVisible.Not())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Install = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Install");

                try
                {
                    IsBusy.Value = true;
                    StatusText.Value = ExtensionsPage.Installing;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        if (_app.AuthorizedUser.Value != null)
                        {
                            await _app.AuthorizedUser.Value.RefreshAsync();
                        }

                        Release release = await AcquirePackage();
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                        try
                        {
                            await _handler.DownloadAndLoadPackage(release, packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsPage.PackageInstaller,
                                message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation,
                                    packageId.Id));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Immediate install failed, falling back to queue.");
                            _handler.Queue.InstallQueue(packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsPage.PackageInstaller,
                                message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation,
                                    packageId.Id));
                        }
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    StatusText.Value = null;
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Update");

                try
                {
                    IsBusy.Value = true;
                    StatusText.Value = ExtensionsPage.Updating;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        if (_app.AuthorizedUser.Value != null)
                        {
                            await _app.AuthorizedUser.Value.RefreshAsync();
                        }

                        Release release = await AcquirePackage();
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                        try
                        {
                            _handler.UnloadPackages(Package.Name);

                            _handler.DeleteOldVersionFiles(Package.Name);

                            await _handler.DownloadAndLoadPackage(release, packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsPage.PackageInstaller,
                                message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUpdate, packageId.Id));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Immediate update failed, falling back to queue.");
                            _handler.Queue.InstallQueue(packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsPage.PackageInstaller,
                                message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUpdate, packageId.Id));
                        }
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    StatusText.Value = null;
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Uninstall = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Uninstall");

                try
                {
                    IsBusy.Value = true;
                    StatusText.Value = ExtensionsPage.Uninstalling;

                    if (!_handler.UnloadPackages(Package.Name))
                    {
                        throw new Exception("Failed to unload the package. It may still be in use. Uninstallation has been scheduled.");
                    }

                    bool hasFallback = _handler.UninstallWithFallback(Package.Name);

                    if (hasFallback)
                    {
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation, Package.Name));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
                    _handler.QueueUninstallAll(Package.Name);
                    NotificationService.ShowInformation(
                        title: ExtensionsPage.PackageInstaller,
                        message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation, Package.Name));
                }
                finally
                {
                    StatusText.Value = null;
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Cancel = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                try
                {
                    IsBusy.Value = true;
                    _handler.Cancel(Package.Name);
                }
                catch (Exception e)
                {
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        RemoveFromLibrary = new AsyncReactiveCommand(IsBusy.Not().AreTrue(_app.AuthorizedUser.Select(i => i != null)))
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.RemoveFromLibrary");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        if (_app.AuthorizedUser.Value != null)
                        {
                            await _app.AuthorizedUser.Value.RefreshAsync();
                            await _library.RemovePackage(Package);
                        }

                        activity?.AddEvent(new("Removed_PackageFromLibrary"));
                        OnRemoveFromLibrary?.Invoke(this);
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);
    }

    private async Task<Release> AcquirePackage()
    {
        if (_app.AuthorizedUser.Value != null)
        {
            return await _library.Acquire(Package);
        }

        return (await Package.GetReleasesAsync())[0];
    }

    public Package Package { get; }

    public string Name => Package.Name;

    public IReadOnlyReactiveProperty<string?> DisplayName => Package.DisplayName;

    public IReadOnlyReactiveProperty<string?> LogoUrl => Package.LogoUrl;

    public string Publisher => Package.Owner.Name;

    public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUpdateButtonVisible { get; }

    public ReactivePropertySlim<Release?> LatestRelease { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

    public AsyncReactiveCommand Install { get; }

    public AsyncReactiveCommand Update { get; }

    public AsyncReactiveCommand Uninstall { get; }

    public AsyncReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<string?> StatusText { get; } = new();

    public AsyncReactiveCommand RemoveFromLibrary { get; }

    public Action<RemoteUserPackageViewModel>? OnRemoveFromLibrary { get; set; }

    IReadOnlyReactiveProperty<bool> IUserPackageViewModel.IsUpdateButtonVisible => IsUpdateButtonVisible;

    bool IUserPackageViewModel.IsRemote => true;

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
