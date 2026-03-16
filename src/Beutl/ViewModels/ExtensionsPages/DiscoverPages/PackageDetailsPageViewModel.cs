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

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel, ISupportRefreshViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<PackageDetailsPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly PackageOperationHandler _handler;
    private readonly LibraryService _library;
    private readonly BeutlApiApplication _app;

    public PackageDetailsPageViewModel(Package package, BeutlApiApplication app)
    {
        Package = package;
        _app = app;
        _handler = new PackageOperationHandler(app);
        _library = app.GetResource<LibraryService>();

        DisplayName = package.DisplayName
            .Select(x => !string.IsNullOrWhiteSpace(x) ? x : Package.Name)
            .ToReadOnlyReactivePropertySlim(Package.Name)
            .DisposeWith(_disposables);

        Refresh = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("PackageDetailsPage.Refresh");

                try
                {
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        IsBusy.Value = true;
                        AllReleases.Clear();
                        LatestRelease.Value = null;
                        await Package.RefreshAsync();

                        int totalCount = 0;
                        int prevCount = 0;

                        do
                        {
                            Release[] array = await package.GetReleasesAsync(totalCount, 30);
                            AllReleases.AddRange(array);

                            if (LatestRelease.Value == null && array.FirstOrDefault() is { } publicRelease)
                            {
                                LatestRelease.Value = publicRelease;
                                SelectedRelease.Value = publicRelease;
                            }

                            totalCount += array.Length;
                            prevCount = array.Length;
                        } while (prevCount == 30);

                        if (_handler.InstalledPackageRepository.ExistsPackage(package.Name))
                        {
                            PackageIdentity mostLatested = _handler.InstalledPackageRepository.GetLocalPackages(package.Name)
                                .Aggregate((x, y) => x.Version > y.Version ? x : y);

                            CurrentRelease.Value = await package.GetReleaseAsync(mostLatested.Version.ToString());
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
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Refresh.Execute();

        IObservable<PackageChangesQueue.EventType> observable = _handler.Queue.GetObservable(package.Name);
        CanCancel = observable.Select(x => x != PackageChangesQueue.EventType.None)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanInstallOrUpdate = SelectedRelease.Select(v =>
            {
                string beutlVersion = BeutlApplication.Version;

                if (v?.TargetVersion?.Value is { } target
                    && VersionRange.TryParse(target, out VersionRange? versionRange)
                    && NuGetVersion.TryParse(beutlVersion, out NuGetVersion? version))
                {
                    return versionRange.Satisfies(version);
                }
                else
                {
                    return true;
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<bool> installed = _handler.InstalledPackageRepository.GetObservable(package.Name);
        IsInstallButtonVisible = installed.Not()
            .AreTrue(CanCancel.Not(), CanInstallOrUpdate)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUpdateButtonVisible = CurrentRelease.CombineLatest(SelectedRelease)
            .Select(x => x.First != null && x.First.Version.Value != x.Second?.Version.Value)
            .AreTrue(CanCancel.Not(), CanInstallOrUpdate)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUninstallButtonVisible = installed
            .AreTrue(CanCancel.Not())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Downgrade = SelectedRelease.CombineLatest(CurrentRelease)
            .Select(x =>
            {
                if (x.First is { } selected && x.Second is { } current)
                {
                    return NuGetVersion.Parse(selected.Version.Value)
                        .CompareTo(NuGetVersion.Parse(current.Version.Value)) < 0;
                }
                else
                {
                    return false;
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SelectingLatestVersion = SelectedRelease.CombineLatest(LatestRelease)
            .Select(x => x.First?.Version?.Value == x.Second?.Version?.Value)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        InstallButtonText = Package.FormattedPrice.CombineLatest(Package.Owned)
            .Select(t => (t.Second ? null : t.First) ?? ExtensionsStrings.Install)
            .ToReadOnlyReactivePropertySlim(ExtensionsStrings.Install)
            .DisposeWith(_disposables);

        Install = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("PackageDetailsPage.Install");

                try
                {
                    // 価格が設定されていて所有していない場合はストアページを開く
                    if (Package.Price.Value != null && Package.Price.Value > 0 && !Package.Owned.Value)
                    {
                        string url = $"https://beutl.beditor.net/store/{Package.Name}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        return;
                    }

                    StatusText.Value = ExtensionsStrings.Installing;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        Release release = await AcquireRelease();
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                        try
                        {
                            await _handler.DownloadAndLoadPackage(release, packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsStrings.PackageInstaller,
                                message: string.Format(ExtensionsStrings.PackageInstaller_Installed,
                                    packageId.Id));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Immediate install failed, falling back to queue.");
                            _handler.Queue.InstallQueue(packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsStrings.PackageInstaller,
                                message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledInstallation,
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
                }
            })
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("PackageDetailsPage.Update");

                try
                {
                    if (!await PackageOperationHandler.EnsureProjectClosed())
                        return;

                    StatusText.Value = ExtensionsStrings.Updating;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        Release release = await AcquireRelease();
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                        try
                        {
                            await _handler.UnloadPackages(Package.Name);

                            _handler.DeleteOldVersionFiles(Package.Name);

                            await _handler.DownloadAndLoadPackage(release, packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsStrings.PackageInstaller,
                                message: string.Format(ExtensionsStrings.PackageInstaller_Updated, packageId.Id));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Immediate update failed, falling back to queue.");
                            _handler.Queue.InstallQueue(packageId);
                            NotificationService.ShowInformation(
                                title: ExtensionsStrings.PackageInstaller,
                                message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUpdate, packageId.Id));
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
                }
            })
            .DisposeWith(_disposables);

        Uninstall = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                try
                {
                    if (!await PackageOperationHandler.EnsureProjectClosed())
                        return;

                    StatusText.Value = ExtensionsStrings.Uninstalling;

                    if (!await _handler.UnloadPackages(Package.Name))
                    {
                        throw new Exception("Failed to unload the package. It may still be in use. Uninstallation has been scheduled.");
                    }

                    if (_handler.UninstallWithFallback(Package.Name))
                    {
                        NotificationService.ShowInformation(
                            title: ExtensionsStrings.PackageInstaller,
                            message: string.Format(ExtensionsStrings.PackageInstaller_Uninstalled, Package.Name));
                    }
                    else
                    {
                        NotificationService.ShowInformation(
                            title: ExtensionsStrings.PackageInstaller,
                            message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUninstallation, Package.Name));
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
                    _handler.QueueUninstallAll(Package.Name);
                    NotificationService.ShowInformation(
                        title: ExtensionsStrings.PackageInstaller,
                        message: string.Format(ExtensionsStrings.PackageInstaller_ScheduledUninstallation, Package.Name));
                }
                finally
                {
                    StatusText.Value = null;
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
    }

    private async Task<Release> AcquireRelease()
    {
        if (_app.AuthenticatedUser.Value != null)
        {
            await _app.AuthenticatedUser.Value.RefreshAsync();
            await _library.Acquire(Package);
        }

        if (SelectedRelease.Value != null)
        {
            return SelectedRelease.Value;
        }

        return (await Package.GetReleasesAsync())[0];
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<string> DisplayName { get; }

    public CoreList<Release> AllReleases { get; } = [];

    public ReactivePropertySlim<Release?> SelectedRelease { get; } = new();

    public ReactivePropertySlim<Release?> LatestRelease { get; } = new();

    public ReactivePropertySlim<Release?> CurrentRelease { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUpdateButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

    public ReadOnlyReactivePropertySlim<bool> Downgrade { get; }

    public ReadOnlyReactivePropertySlim<bool> SelectingLatestVersion { get; }

    public ReadOnlyReactivePropertySlim<bool> CanInstallOrUpdate { get; }

    public ReadOnlyReactivePropertySlim<string> InstallButtonText { get; }

    public AsyncReactiveCommand Install { get; }

    public AsyncReactiveCommand Update { get; }

    public AsyncReactiveCommand Uninstall { get; }

    public AsyncReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<string?> StatusText { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
