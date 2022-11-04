using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

using NuGet.Packaging.Core;
using NuGet.Versioning;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public sealed class LocalYourPackageViewModel : BaseViewModel, IYourPackageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly InstalledPackageRepository _installedPackageRepository;
    private readonly PackageChangesQueue _queue;
    private readonly PackageIdentity _packageIdentity;

    public LocalYourPackageViewModel(LocalPackage package, BeutlApiApplication app)
    {
        Package = package;
        _packageIdentity = new PackageIdentity(package.Name, new NuGetVersion(package.Version));
        DisplayName = new ReactivePropertySlim<string>(package.DisplayName);
        LogoUrl = new ReactivePropertySlim<string>(package.Logo);

        _installedPackageRepository = app.GetResource<InstalledPackageRepository>();
        _queue = app.GetResource<PackageChangesQueue>();

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
                try
                {
                    IsBusy.Value = true;
                    _queue.InstallQueue(_packageIdentity);
                    Notification.Show(new Notification(
                        Title: "パッケージインストーラー",
                        Message: $"'{_packageIdentity}'のインストールを予約しました。\nパッケージの変更を適用するには、Beutlを終了してください。"));

                    await Task.CompletedTask;
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                }
                finally
                {
                    IsBusy.Value = false;
                }
            })
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(() =>
            {
                try
                {
                    IsBusy.Value = true;
                    if (LatestRelease.Value != null)
                    {
                        var packageId = new PackageIdentity(Package.Name, new NuGetVersion(LatestRelease.Value.Version.Value));
                        _queue.InstallQueue(packageId);
                        Notification.Show(new Notification(
                            Title: "パッケージインストーラー",
                            Message: $"'{packageId}'の更新を予約しました。\nパッケージの変更を適用するには、Beutlを終了してください。"));
                    }
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                }
                finally
                {
                    IsBusy.Value = false;
                }

                return Task.CompletedTask;
            })
            .DisposeWith(_disposables);

        Uninstall = new ReactiveCommand(IsBusy.Not())
            .WithSubscribe(() =>
            {
                try
                {
                    IsBusy.Value = true;
                    _queue.UninstallQueue(_packageIdentity);
                    Notification.Show(new Notification(
                        Title: "パッケージインストーラー",
                        Message: $"'{_packageIdentity}'のアンインストールを予約しました。\nパッケージの変更を適用するには、Beutlを終了してください。"));
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
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
                    _queue.Cancel(_packageIdentity.Id);
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
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

    public ReactiveCommand Uninstall { get; }

    public ReactiveCommand Cancel { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    IReadOnlyReactiveProperty<bool> IYourPackageViewModel.IsUpdateButtonVisible => IsUpdateButtonVisible;

    bool IYourPackageViewModel.IsRemote => false;

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
