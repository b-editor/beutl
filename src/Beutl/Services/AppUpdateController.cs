using System.Reactive.Subjects;
using Avalonia.Threading;
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed record AppUpdateStatus(string? Text, bool IsBusy, double? Progress = null)
{
    public static AppUpdateStatus Idle { get; } = new(null, false);

    public bool IsActive => Text != null;
}

// Runs the one update of this session, so it never takes the editor away from the user: it prepares
// in the background, says when it is ready, and shows its dialog only when asked to.
internal sealed class AppUpdateController
{
    private static readonly TimeSpan s_readyNotificationDuration = TimeSpan.FromSeconds(30);
    private static readonly ILogger s_logger = Log.CreateLogger<AppUpdateController>();
    private readonly Func<AppUpdateResponse, UpdateDialogViewModel> _createViewModel;
    private readonly Func<bool> _downloadsAutomatically;
    private readonly BehaviorSubject<UpdateDialogViewModel?> _current = new(null);
    private bool _isDialogOpen;

    // Test seam: tests that never start an update unasked leave downloadsAutomatically out.
    internal AppUpdateController(
        Func<AppUpdateResponse, UpdateDialogViewModel> createViewModel, Func<bool>? downloadsAutomatically = null)
    {
        _createViewModel = createViewModel;
        _downloadsAutomatically = downloadsAutomatically ?? (() => false);
        Status = _current
            .Select(viewModel => viewModel == null
                ? Observable.Return(AppUpdateStatus.Idle)
                : viewModel.Phase.CombineLatest(viewModel.ProgressText, viewModel.DownloadProgress,
                    (phase, text, progress) => new AppUpdateStatus(text ?? "", phase == AppUpdatePhase.Preparing, progress)))
            .Switch();
    }

    public static AppUpdateController Instance { get; } = new(
        update => new UpdateDialogViewModel(update),
        () => CanDownloadAutomatically && GlobalConfiguration.Instance.UpdateConfig.AutoDownload.Value);

    // A Flatpak update installs as it prepares, which can ask for an administrator's password, so it never starts
    // unasked. The settings leave out the switch where it would do nothing.
    internal static bool CanDownloadAutomatically => !FlatpakUpdater.IsRunning && !FlatpakUpdater.RequiresManualUpdate;

    public IObservable<AppUpdateStatus> Status { get; }

    internal UpdateDialogViewModel? Current => _current.Value;

    // Starts an update that was found rather than asked for, when the settings let it download unasked. False when it
    // is left for the user to start.
    public bool TryStartAutomatically(AppUpdateResponse update)
    {
        if (!_downloadsAutomatically() || Start(update) is not { } viewModel) return false;

        AnnounceDownload(viewModel);
        return true;
    }

    public void StartInBackground(AppUpdateResponse update)
    {
        // Asking again for an update that is already under way shows how far it has got.
        if (Start(update) == null)
        {
            ShowDialog();
        }
    }

    public void StartAndShowDialog(AppUpdateResponse update)
    {
        // The dialog opens first, so the outcome of the update is shown there rather than notified.
        Start(update, ShowDialog);
    }

    public void ShowDialog()
    {
        if (_isDialogOpen || _current.Value is not { } viewModel) return;

        _isDialogOpen = true;
        _ = ShowDialogAsync(viewModel);
    }

    public static async void ScheduleInstallOnQuit(UpdateDialogViewModel viewModel)
    {
        // Leaving a preparing update running needs nothing more.
        if (viewModel.Phase.Value != AppUpdatePhase.Ready) return;

        if (await viewModel.ScheduleInstallOnQuitAsync())
        {
            NotificationService.ShowInformation(
                MessageStrings.UpdateAction_UpdateOnQuit,
                MessageStrings.UpdateWillInstallOnQuit);
        }
        else if (!viewModel.CancellationToken.IsCancellationRequested)
        {
            NotificationService.ShowError(MessageStrings.UpdateFailed_Title, viewModel.ProgressText.Value);
        }
    }

    internal static string GetVersionLabel(string version) => $"Beutl {version}";

    // The update that started, or null when one was already under way.
    private UpdateDialogViewModel? Start(AppUpdateResponse update, Action? beforeStart = null)
    {
        if (_current.Value != null)
        {
            beforeStart?.Invoke();
            return null;
        }

        UpdateDialogViewModel viewModel = _createViewModel(update);
        viewModel.Phase.Skip(1).Subscribe(phase => OnPhaseChanged(viewModel, phase));
        _current.OnNext(viewModel);
        beforeStart?.Invoke();
        viewModel.Start();
        return viewModel;
    }

    // Downloading asks for nothing, so this says only what the title bar's ring is doing, as briefly as any other
    // notice. It gives way to the notification of the outcome, also in the list of earlier notifications, so the
    // update has one notification at a time.
    private static void AnnounceDownload(UpdateDialogViewModel viewModel)
    {
        var announcement = new CancellationTokenSource();
        // Taken first: an update that already left Preparing disposes the source as soon as this subscribes.
        CancellationToken withdrawn = announcement.Token;
        viewModel.Phase.Where(phase => phase != AppUpdatePhase.Preparing).Take(1)
            .Subscribe(_ =>
            {
                announcement.Cancel();
                announcement.Dispose();
            });
        NotificationService.Show(new Notification(
            MessageStrings.UpdateDownloading_Title,
            GetVersionLabel(viewModel.Update.LatestVersion),
            Actions: viewModel.ReleaseUri is { } releaseUri ? [ReleaseNotesAction(releaseUri)] : null)
        {
            CancellationToken = withdrawn,
        });
    }

    private async Task ShowDialogAsync(UpdateDialogViewModel viewModel)
    {
        try
        {
            await new UpdateDialog { DataContext = viewModel }.ShowAsync();
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void OnPhaseChanged(UpdateDialogViewModel viewModel, AppUpdatePhase phase)
    {
        // The update prepares on the thread pool, while the dialog opens and closes on the UI thread. Deciding there
        // keeps the decision in order with the dialog, so the outcome is neither lost nor shown twice.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPhaseChanged(viewModel, phase));
            return;
        }

        if (phase is AppUpdatePhase.Canceled or AppUpdatePhase.Completed && _current.Value == viewModel)
        {
            _current.OnNext(null);
        }

        // An open dialog is already showing the outcome.
        if (_isDialogOpen) return;

        switch (phase)
        {
            case AppUpdatePhase.Ready:
                NotificationService.Show(new Notification(
                    MessageStrings.UpdateReady_Title,
                    GetVersionLabel(viewModel.Update.LatestVersion),
                    NotificationType.Success,
                    s_readyNotificationDuration,
                    Actions: CreateReadyActions(viewModel))
                {
                    CancellationToken = viewModel.CancellationToken,
                });
                break;
            case AppUpdatePhase.Failed:
                NotificationService.Show(new Notification(
                    MessageStrings.UpdateFailed_Title,
                    viewModel.ProgressText.Value,
                    NotificationType.Error,
                    s_readyNotificationDuration,
                    Actions: [new(MessageStrings.UpdateAction_Retry, viewModel.Retry)])
                {
                    CancellationToken = viewModel.CancellationToken,
                });
                break;
            case AppUpdatePhase.Completed:
                NotificationService.ShowSuccess(
                    GetVersionLabel(viewModel.Update.LatestVersion),
                    viewModel.ProgressText.Value);
                break;
        }
    }

    private NotificationAction[] CreateReadyActions(UpdateDialogViewModel viewModel)
    {
        NotificationAction installNow = new(viewModel.PrimaryButtonText.Value, () => InstallNow(viewModel));
        NotificationAction onQuit = new(MessageStrings.UpdateAction_UpdateOnQuit, () => ScheduleInstallOnQuit(viewModel));
        // The notice of an update that downloaded unasked is gone by now, so the release notes are offered here too.
        return viewModel.ReleaseUri is { } releaseUri
            ? [installNow, onQuit, ReleaseNotesAction(releaseUri)]
            : [installNow, onQuit];
    }

    // Opening the release notes leaves the notification on screen, with whatever choice it offers.
    private static NotificationAction ReleaseNotesAction(Uri releaseUri)
        => new(MessageStrings.ReleaseNotes, () => OpenUrl(releaseUri.AbsoluteUri), DismissOnInvoke: false);

    // The dialog stays open through the handoff, so a failure there can be read.
    private void InstallNow(UpdateDialogViewModel viewModel)
    {
        ShowDialog();
        _ = viewModel.HandlePrimaryButtonClick();
    }

    // Opens a page in the browser. False when it did not open; the user is told why.
    internal static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            s_logger.LogError(ex, "Failed to open URL: {Url}", url);
            NotificationService.ShowError(Strings.Error, ex.Message);
            return false;
        }
    }
}
