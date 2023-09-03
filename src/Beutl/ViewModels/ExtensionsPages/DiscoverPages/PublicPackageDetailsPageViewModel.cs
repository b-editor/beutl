using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Services;

using NuGet.Packaging.Core;
using NuGet.Versioning;

using Reactive.Bindings;

using Serilog;

using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class PublicPackageDetailsPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<PublicPackageDetailsPageViewModel>();
    private readonly CompositeDisposable _disposables = new();
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly LibraryService _library;
    private readonly BeutlApiApplication _app;

    public PublicPackageDetailsPageViewModel(Package package, BeutlApiApplication app)
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
                try
                {
                    using (await _app.Lock.LockAsync())
                    {
                        IsBusy.Value = true;
                        await package.RefreshAsync();
                        int totalCount = 0;
                        int prevCount = 0;

                        do
                        {
                            Release[] array = await package.GetReleasesAsync(totalCount, 30);
                            if (Array.Find(array, x => x.IsPublic.Value) is { } publicRelease)
                            {
                                LatestRelease.Value = publicRelease;
                                break;
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
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
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

        IObservable<bool> installed = _installedPackageRepository.GetObservable(package.Name);
        IsInstallButtonVisible = installed
            .AnyTrue(CanCancel)
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUpdateButtonVisible = LatestRelease.CombineLatest(CurrentRelease)
            .Select(x =>
            {
                if (x.First is { } latest && x.Second is { } current)
                {
                    return NuGetVersion.Parse(latest.Version.Value).CompareTo(NuGetVersion.Parse(current.Version.Value)) > 0;
                }
                else
                {
                    return false;
                }
            })
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
                try
                {
                    IsBusy.Value = true;
                    using(await _app.Lock.LockAsync())
                    {
                        await _app.AuthorizedUser.Value!.RefreshAsync();

                        Release release = await _library.GetPackage(Package);
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));
                        _queue.InstallQueue(packageId);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledInstallation, package));
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
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
                try
                {
                    IsBusy.Value = true;
                    using(await _app.Lock.LockAsync())
                    {
                        await _app.AuthorizedUser.Value!.RefreshAsync();
                        Release release = await _library.GetPackage(Package);
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));
                        _queue.InstallQueue(packageId);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUpdate, packageId));
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
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
                try
                {
                    IsBusy.Value = true;
                    foreach (PackageIdentity item in _installedPackageRepository.GetLocalPackages(Package.Name))
                    {
                        _queue.UninstallQueue(item);
                        NotificationService.ShowInformation(
                            title: ExtensionsPage.PackageInstaller,
                            message: string.Format(ExtensionsPage.PackageInstaller_ScheduledUninstallation, item));
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                    _logger.Error(e, "An unexpected error has occurred.");
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
                    _logger.Error(e, "An unexpected error has occurred.");
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

    public ReactivePropertySlim<Release?> LatestRelease { get; } = new();

    public ReactivePropertySlim<Release?> CurrentRelease { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUpdateButtonVisible { get; }

    public ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

    public AsyncReactiveCommand Install { get; }

    public AsyncReactiveCommand Update { get; }

    public ReactiveCommand Uninstall { get; }

    public ReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
