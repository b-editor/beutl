using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class LocalUserPackageViewModel : BaseViewModel, IUserPackageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<LocalUserPackageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly PackageOperationHandler _handler;
    private readonly PackageIdentity _packageIdentity;

    public LocalUserPackageViewModel(LocalPackage package, BeutlApiApplication app)
    {
        Package = package;
        _packageIdentity = new PackageIdentity(package.Name, new NuGetVersion(package.Version));
        DisplayName = new ReactivePropertySlim<string>(package.DisplayName);
        LogoUrl = new ReactivePropertySlim<string>(package.Logo);

        _handler = new PackageOperationHandler(app);

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
                using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Install");

                try
                {
                    IsBusy.Value = true;
                    if (!await PackageOperationHandler.EnsureProjectClosed())
                        return;

                    StatusText.Value = ExtensionsPage.Installing;

                    try
                    {
                        await _handler.DownloadAndLoadPackage(_packageIdentity);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_Installed,
                                _packageIdentity.Id));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Immediate install failed, falling back to queue.");
                        _handler.Queue.InstallQueue(_packageIdentity);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation,
                                _packageIdentity.Id));
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
                using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Update");

                try
                {
                    IsBusy.Value = true;
                    if (!await PackageOperationHandler.EnsureProjectClosed())
                        return;

                    StatusText.Value = ExtensionsPage.Updating;
                    if (LatestRelease.Value != null)
                    {
                        var packageId = new PackageIdentity(Package.Name,
                            new NuGetVersion(LatestRelease.Value.Version.Value));

                        try
                        {
                            _handler.UnloadPackages(Package.Name);
                            _handler.DeleteOldVersionFiles(Package.Name);
                            await _handler.DownloadAndLoadPackage(LatestRelease.Value, packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsPage.PackageInstaller,
                                message: string.Format(ExtensionsPage.PackageInstaller_Updated, packageId.Id));
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
                using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Uninstall");

                try
                {
                    IsBusy.Value = true;
                    if (!await PackageOperationHandler.EnsureProjectClosed())
                        return;

                    StatusText.Value = ExtensionsPage.Uninstalling;

                    if (!_handler.UnloadPackages(Package.Name))
                    {
                        throw new Exception("Failed to unload the package. It may still be in use. Uninstallation has been scheduled.");
                    }

                    if (!_handler.UninstallSinglePackage(Package.InstalledPath, _packageIdentity))
                    {
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation,
                                _packageIdentity.Id));
                    }
                    else
                    {
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_Uninstalled,
                                _packageIdentity.Id));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
                    _handler.Queue.UninstallQueue(_packageIdentity);
                    NotificationService.ShowInformation(
                        title: ExtensionsPage.PackageInstaller,
                        message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation,
                            _packageIdentity.Id));
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
                    _handler.Cancel(_packageIdentity.Id);
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
    }

    public LocalPackage Package { get; }

    public string Name => Package.Name;

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string> LogoUrl { get; }

    public string Publisher => Package.Publisher;

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

    IReadOnlyReactiveProperty<bool> IUserPackageViewModel.IsUpdateButtonVisible => IsUpdateButtonVisible;

    bool IUserPackageViewModel.IsRemote => false;

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
