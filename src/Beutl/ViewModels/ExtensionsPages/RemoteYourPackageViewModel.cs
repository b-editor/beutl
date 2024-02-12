using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using NuGet.Packaging.Core;
using NuGet.Versioning;

using OpenTelemetry.Trace;

using Reactive.Bindings;

using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class RemoteYourPackageViewModel : BaseViewModel, IYourPackageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<RemoteYourPackageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly BeutlApiApplication _app;
    private readonly LibraryService _library;

    public RemoteYourPackageViewModel(Package package, BeutlApiApplication app)
    {
        Package = package;
        _app = app;
        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();
        _library = app.GetResource<LibraryService>();

        IObservable<PackageChangesQueue.EventType> observable = _queue.GetObservable(package.Name);
        CanCancel = observable.Select(x => x != PackageChangesQueue.EventType.None)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<bool> installed = _installedPackageRepository.GetObservable(package.Name);
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
                using Activity? activity = Telemetry.StartActivity("RemoteYourPackage.Install");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        await _app.AuthorizedUser.Value!.RefreshAsync();

                        Release release = await _library.GetPackage(Package);

                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));
                        _queue.InstallQueue(packageId);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation, packageId.Id));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteYourPackage.Update");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        await _app.AuthorizedUser.Value!.RefreshAsync();

                        Release release = await _library.GetPackage(Package);

                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));
                        _queue.InstallQueue(packageId);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUpdate, packageId.Id));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Uninstall = new ReactiveCommand(IsBusy.Not())
            .WithSubscribe(() =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteYourPackage.Uninstall");

                try
                {
                    IsBusy.Value = true;
                    foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(Package.Name))
                    {
                        _queue.UninstallQueue(item);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation, item.Id));
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Cancel = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                try
                {
                    IsBusy.Value = true;
                    _queue.Cancel(Package.Name);
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        RemoveFromLibrary = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("RemoteYourPackage.RemoveFromLibrary");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        await _app.AuthorizedUser.Value!.RefreshAsync();

                        await _library.RemovePackage(Package);
                        activity?.AddEvent(new("Removed_PackageFromLibrary"));
                        OnRemoveFromLibrary?.Invoke(this);
                    }
                }
                catch (Exception e)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    ErrorHandle(e);
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);
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

    public ReactiveCommand Uninstall { get; }

    public ReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand RemoveFromLibrary { get; }

    public Action<RemoteYourPackageViewModel>? OnRemoveFromLibrary { get; set; }

    IReadOnlyReactiveProperty<bool> IYourPackageViewModel.IsUpdateButtonVisible => IsUpdateButtonVisible;

    bool IYourPackageViewModel.IsRemote => true;

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
