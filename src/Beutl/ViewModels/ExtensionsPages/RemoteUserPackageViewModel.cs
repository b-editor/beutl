using Avalonia.Controls;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;
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

    public RemoteUserPackageViewModel(Package package, BeutlApiApplication app, EditorService editorService, ProjectService projectService)
    {
        Package = package;
        _app = app;
        _handler = new PackageOperationHandler(app, editorService, projectService);
        _library = app.GetResource<LibraryService>();

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

        RemoveFromLibrary = new AsyncReactiveCommand(IsBusy.Not().AreTrue(_app.AuthenticatedUser.Select(i => i != null)))
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

                Release release = await AcquirePackage();
                var packageId = new PackageIdentity(Package.Name, new NuGetVersion(release.Version.Value));

                await _handler.InstallOrQueueAsync(release, packageId, _logger, CancellationToken.None);
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

    private async Task UpdateAsync()
    {
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Update");

        try
        {
            IsBusy.Value = true;
            if (!await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Updating;
            using (await _app.Lock.LockAsync())
            {
                activity?.AddEvent(new("Entered_AsyncLock"));
                if (_app.AuthenticatedUser.Value != null)
                {
                    await _app.AuthenticatedUser.Value.RefreshAsync(CancellationToken.None);
                }

                Release release = await AcquirePackage();
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
        using Activity? activity = Telemetry.StartActivity("RemoteUserPackage.Uninstall");

        try
        {
            IsBusy.Value = true;
            if (!await _handler.EnsureProjectClosed())
                return;

            StatusText.Value = ExtensionsStrings.Uninstalling;

            if (await _handler.UnloadAndUninstallAsync(Package.Name))
            {
                PackageNotifications.Uninstalled(Package.Name);
            }
            else
            {
                PackageNotifications.ScheduledUninstallation(Package.Name);
            }
        }
        catch (Exception e)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogWarning(e, "Immediate uninstall failed, falling back to queue.");
            _handler.QueueUninstallAll(Package.Name);
            PackageNotifications.ScheduledUninstallation(Package.Name);
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
            await e.Handle();
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
            await e.Handle();
            _logger.LogError(e, "An unexpected error has occurred.");
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    private async Task<Release> AcquirePackage()
    {
        if (_app.AuthenticatedUser.Value != null)
        {
            return await _library.Acquire(Package, CancellationToken.None);
        }

        return await PackageReleaseResolver.GetFirstReleaseAsync(Package, CancellationToken.None);
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
