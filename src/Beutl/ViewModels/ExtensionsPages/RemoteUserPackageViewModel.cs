using Avalonia.Controls;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using LibraryService = Beutl.Api.Services.LibraryService;

namespace Beutl.ViewModels.ExtensionsPages;

public sealed class RemoteUserPackageViewModel : IUserPackageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<RemoteUserPackageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly PackageOperationHandler _handler;
    private readonly BeutlApiApplication _app;
    private readonly LibraryService _library;

    public RemoteUserPackageViewModel(Package package, BeutlApiApplication app, EditorService editorService, ProjectService projectService)
    {
        Package = package;
        _app = app;
        _handler = new PackageOperationHandler(app, editorService, projectService);
        _library = app.GetResource<LibraryService>();

        (CanCancel, IsInstallButtonVisible, IsUpdateButtonVisible, IsUninstallButtonVisible) = PackageButtonStates.Create(
            _handler, package.Name, IsBusy, LatestRelease, _disposables);

        Install = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);

        Update = new AsyncReactiveCommand(IsUpdateButtonVisible)
            .WithSubscribe(UpdateAsync)
            .DisposeWith(_disposables);

        Uninstall = new AsyncReactiveCommand(IsBusy.Inverse())
            .WithSubscribe(UninstallAsync)
            .DisposeWith(_disposables);

        Cancel = new AsyncReactiveCommand()
            .WithSubscribe(CancelAsync)
            .DisposeWith(_disposables);

        Restart = new AsyncReactiveCommand()
            .WithSubscribe(_handler.RestartAsync)
            .DisposeWith(_disposables);

        RemoveFromLibrary = new AsyncReactiveCommand(
                new[] { IsBusy.Inverse(), _app.AuthenticatedUser.Select(i => i != null) }.CombineLatestValuesAreAllTrue())
            .WithSubscribe(RemoveFromLibraryAsync)
            .DisposeWith(_disposables);
    }

    private async Task InstallAsync()
    {
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Install");

        try
        {
            IsBusy.Value = true;

            StatusText.Value = ExtensionsStrings.Installing;
            using (await _app.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                if (_app.AuthenticatedUser.Value != null)
                {
                    await _app.AuthenticatedUser.Value.RefreshAsync(CancellationToken.None);
                }

                Release? release = await AcquirePackage();
                if (release is null)
                {
                    _handler.Notifications.ShowWarning(Package.Name, ExtensionsStrings.This_extension_does_not_support_your_Beutl_version);
                    return;
                }

                var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                await _handler.InstallOrQueueAsync(release, packageId, _logger, CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await e.Handle(_handler.Notifications);
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
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Update");

        try
        {
            IsBusy.Value = true;
            if (LatestRelease.Value is not { } release || !await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Updating;
            using (await _app.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                if (_app.AuthenticatedUser.Value != null)
                {
                    await _app.AuthenticatedUser.Value.RefreshAsync(CancellationToken.None);
                    await _library.Acquire(Package, CancellationToken.None);
                }

                var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                await _handler.UpdateOrQueueAsync(
                    Package.Name,
                    packageId,
                    token => _handler.DownloadAndLoadPackage(release, packageId, token),
                    _logger,
                    CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await e.Handle(_handler.Notifications);
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
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Uninstall");

        try
        {
            IsBusy.Value = true;
            if (!await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Uninstalling;

            using var lease = await _app.Lock.LockAsync();
            // A project can open while this waits for the lock, and unloading must not race it.
            if (!await _handler.EnsureProjectClosed())
                return;

            if (await _handler.UnloadAndUninstallAsync(Package.Name))
            {
                PackageNotifications.Uninstalled(_handler.Notifications, Package.Name);
            }
            else
            {
                PackageNotifications.ScheduledUninstallation(_handler.Notifications, Package.Name);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
            _handler.QueueUninstallAll(Package.Name);
            PackageNotifications.ScheduledUninstallation(_handler.Notifications, Package.Name);
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
            _handler.Cancel(Package.Name);
        }
        catch (Exception e)
        {
            await e.Handle(_handler.Notifications);
            _logger.LogError(e, "An unexpected error has occurred.");
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    private async Task RemoveFromLibraryAsync()
    {
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.RemoveFromLibrary");

        try
        {
            IsBusy.Value = true;

            // 所有しているが支払っていない場合（入手後に価格が設定された）は確認ダイアログを表示する
            if (!Package.Paid.Value && Package.FormattedPrice.Value != null)
            {
                string priceText = Package.FormattedPrice.Value;
                var dialog = new FAContentDialog
                {
                    Title = ExtensionsStrings.RemoveFromLibrary_Title,
                    Content = new TextBlock
                    {
                        Text = string.Format(ExtensionsStrings.RemoveFromLibrary_PaidConfirmation, priceText),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    PrimaryButtonText = ExtensionsStrings.RemoveFromLibrary,
                    CloseButtonText = Strings.Cancel
                };

                if (await dialog.ShowAsync() is not FAContentDialogResult.Primary)
                    return;
            }

            using (await _app.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                if (_app.AuthenticatedUser.Value != null)
                {
                    await _app.AuthenticatedUser.Value.RefreshAsync(CancellationToken.None);
                    await _library.RemovePackage(Package, CancellationToken.None);
                }

                activity?.AddEvent(new("Removed_PackageFromLibrary"));
                OnRemoveFromLibrary?.Invoke(this);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await e.Handle(_handler.Notifications);
            _logger.LogError(e, "An unexpected error has occurred.");
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    private async Task<Release?> AcquirePackage()
    {
        if (_app.AuthenticatedUser.Value != null)
        {
            Release latest = await _library.Acquire(Package, CancellationToken.None);
            if (latest.CanInstall())
                return latest;
        }

        return await Package.GetLatestCompatibleReleaseAsync(CancellationToken.None);
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

    public AsyncReactiveCommand Restart { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public ReactivePropertySlim<string?> StatusText { get; } = new();

    public AsyncReactiveCommand RemoveFromLibrary { get; }

    public Action<RemoteUserPackageViewModel>? OnRemoveFromLibrary { get; set; }

    IReadOnlyReactiveProperty<bool> IUserPackageViewModel.IsUpdateButtonVisible => IsUpdateButtonVisible;

    bool IUserPackageViewModel.IsRemote => true;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
