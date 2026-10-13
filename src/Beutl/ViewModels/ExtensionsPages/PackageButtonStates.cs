using Beutl.Api.Objects;
using Beutl.Api.Services;
using NuGet.Packaging.Core;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.ExtensionsPages;

// The cancel and install/update/uninstall button states of a package row in the library.
internal static class PackageButtonStates
{
    public static (
        ReadOnlyReactivePropertySlim<bool> CanCancel,
        ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible,
        ReadOnlyReactivePropertySlim<bool> IsUpdateButtonVisible,
        ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible) Create(
        PackageOperationHandler handler,
        string packageName,
        ReactivePropertySlim<bool> isBusy,
        ReactivePropertySlim<Release?> latestRelease,
        CompositeDisposable disposables)
    {
        IObservable<PackageChangesQueue.EventType> observable = handler.Queue.GetObservable(packageName);
        ReadOnlyReactivePropertySlim<bool> canCancel = observable.Select(x => x != PackageChangesQueue.EventType.None)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        IObservable<bool> installed = handler.InstalledPackageRepository.GetObservable(packageName);
        IObservable<bool> notBusy = isBusy.Inverse();
        ReadOnlyReactivePropertySlim<bool> isInstallButtonVisible = new[]
            {
                new[] { installed, canCancel }.CombineLatestValuesAreAllFalse(),
                notBusy
            }
            .CombineLatestValuesAreAllTrue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        IObservable<PackageIdentity?> installedPackage = handler.InstalledPackageRepository.GetPackageObservable(packageName);
        ReadOnlyReactivePropertySlim<bool> isUpdateButtonVisible = new[]
            {
                latestRelease.CombineLatest(installedPackage)
                    .Select(x => PackageUpdateAvailability.IsAvailable(x.First?.Version.Value, x.Second)),
                canCancel.Inverse(),
                notBusy
            }
            .CombineLatestValuesAreAllTrue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<bool> isUninstallButtonVisible = new[]
            {
                installed,
                canCancel.Inverse(),
                isUpdateButtonVisible.Inverse(),
                notBusy
            }
            .CombineLatestValuesAreAllTrue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        return (canCancel, isInstallButtonVisible, isUpdateButtonVisible, isUninstallButtonVisible);
    }
}
