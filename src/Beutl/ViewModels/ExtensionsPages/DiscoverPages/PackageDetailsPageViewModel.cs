﻿using Beutl.Api;
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
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly LibraryService _library;
    private readonly BeutlApiApplication _app;

    public PackageDetailsPageViewModel(Package package, BeutlApiApplication app)
    {
        Package = package;
        _app = app;
        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();
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

                        if (_installedPackageRepository.ExistsPackage(package.Name))
                        {
                            PackageIdentity mostLatested = _installedPackageRepository.GetLocalPackages(package.Name)
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

        IObservable<PackageChangesQueue.EventType> observable = _queue.GetObservable(package.Name);
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

        IObservable<bool> installed = _installedPackageRepository.GetObservable(package.Name);
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

        Install = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("PackageDetailsPage.Install");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        Release? release = null;
                        if (_app.AuthorizedUser.Value != null)
                        {
                            await _app.AuthorizedUser.Value.RefreshAsync();
                            release = await _library.Acquire(Package);
                        }

                        if (SelectedRelease.Value != null)
                        {
                            release = SelectedRelease.Value;
                        }

                        if (release == null)
                        {
                            release = (await Package.GetReleasesAsync())[0];
                        }

                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));
                        _queue.InstallQueue(packageId);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation,
                                packageId.Id));
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

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
                using Activity? activity = Telemetry.StartActivity("PackageDetailsPage.Update");

                try
                {
                    IsBusy.Value = true;
                    using (await _app.Lock.LockAsync())
                    {
                        activity?.AddEvent(new("Entered_AsyncLock"));
                        Release? release = null;
                        if (_app.AuthorizedUser.Value != null)
                        {
                            await _app.AuthorizedUser.Value.RefreshAsync();
                            release = await _library.Acquire(Package);
                        }

                        if (SelectedRelease.Value != null)
                        {
                            release = SelectedRelease.Value;
                        }

                        if (release == null)
                        {
                            release = (await Package.GetReleasesAsync())[0];
                        }

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
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Uninstall = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(async () =>
            {
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
                    await e.Handle();
                    _logger.LogError(e, "An unexpected error has occurred.");
                }
                finally
                {
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
                    _queue.Cancel(Package.Name);
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

    public AsyncReactiveCommand Install { get; }

    public AsyncReactiveCommand Update { get; }

    public AsyncReactiveCommand Uninstall { get; }

    public AsyncReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
