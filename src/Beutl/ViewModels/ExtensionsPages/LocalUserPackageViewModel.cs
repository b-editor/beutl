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

    public LocalUserPackageViewModel(LocalPackage package, BeutlApiApplication app, EditorService editorService, ProjectService projectService)
    {
        Package = package;
        _packageIdentity = new PackageIdentity(package.Name, new NuGetVersion(package.Version));
        DisplayName = new ReactivePropertySlim<string>(package.DisplayName);
        LogoUrl = new ReactivePropertySlim<string>(package.Logo);

        _handler = new PackageOperationHandler(app, editorService, projectService);

        (CanCancel, IsInstallButtonVisible, IsUpdateButtonVisible, IsUninstallButtonVisible) = PackageButtonStates.Create(
            _handler, package.Name, IsBusy, LatestRelease, _disposables);

        Install = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(UpdateAsync)
            .DisposeWith(_disposables);

        Uninstall = new AsyncReactiveCommand(IsBusy.Not())
            .WithSubscribe(UninstallAsync)
            .DisposeWith(_disposables);

        Cancel = new AsyncReactiveCommand()
            .WithSubscribe(CancelAsync)
            .DisposeWith(_disposables);
    }

    private async Task InstallAsync()
    {
        using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Install");

        try
        {
            IsBusy.Value = true;

            StatusText.Value = ExtensionsStrings.Installing;

            await _handler.InstallOrQueueAsync(_packageIdentity, _logger, CancellationToken.None);
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
    }

    private async Task UpdateAsync()
    {
        using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Update");

        try
        {
            IsBusy.Value = true;
            if (!await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Updating;
            if (LatestRelease.Value != null)
            {
                var packageId = new PackageIdentity(Package.Name,
                    new NuGetVersion(LatestRelease.Value.Version.Value));

                await _handler.UpdateOrQueueAsync(
                    Package.Name,
                    packageId,
                    token => _handler.DownloadAndLoadPackage(LatestRelease.Value, packageId, token),
                    _logger,
                    CancellationToken.None);
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
    }

    private async Task UninstallAsync()
    {
        using Activity? activity = Telemetry.StartActivity("LocalUserPackage.Uninstall");

        try
        {
            IsBusy.Value = true;
            if (!await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Uninstalling;

            if (!await _handler.UnloadPackages(Package.Name))
            {
                throw new Exception("Failed to unload the package. It may still be in use. Uninstallation has been scheduled.");
            }

            if (!_handler.UninstallSinglePackage(Package.InstalledPath, _packageIdentity))
            {
                PackageNotifications.ScheduledUninstallation(_packageIdentity.Id);
            }
            else
            {
                PackageNotifications.Uninstalled(_packageIdentity.Id);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
            _handler.Queue.UninstallQueue(_packageIdentity);
            PackageNotifications.ScheduledUninstallation(_packageIdentity.Id);
        }
        finally
        {
            StatusText.Value = null;
            IsBusy.Value = false;
        }
    }

    private async Task CancelAsync()
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
