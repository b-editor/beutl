using System.Reactive.Subjects;
using Avalonia.Threading;
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;
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
    // Where release notes are read when an update names no page of its own.
    internal const string ReleasesPageUrl = "https://github.com/b-editor/beutl/releases";
    private static readonly TimeSpan s_readyNotificationDuration = TimeSpan.FromSeconds(30);
    private static readonly ILogger s_logger = Log.CreateLogger<AppUpdateController>();
    private readonly Func<AppUpdateResponse, UpdateDialogViewModel> _createViewModel;
    private readonly Func<bool> _downloadsAutomatically;
    private readonly BehaviorSubject<UpdateDialogViewModel?> _current = new(null);
    private bool _isDialogOpen;
    private readonly INotificationService _notifications;

    // Test seam: tests that never start an update unasked leave downloadsAutomatically out.
    internal AppUpdateController(
        Func<AppUpdateResponse, UpdateDialogViewModel> createViewModel, INotificationService notifications, Func<bool>? downloadsAutomatically = null)
    {
        _createViewModel = createViewModel;
        _notifications = notifications;
        _downloadsAutomatically = downloadsAutomatically ?? (() => false);
        Status = _current
            .Select(viewModel => viewModel == null
                ? Observable.Return(AppUpdateStatus.Idle)
                : viewModel.Phase.CombineLatest(viewModel.ProgressText, viewModel.DownloadProgress,
                    (phase, text, progress) => new AppUpdateStatus(text ?? "",
                        phase is AppUpdatePhase.Preparing or AppUpdatePhase.Installing, progress)))
            .Switch();
    }

    internal AppUpdateController(UpdateConfig configuration, INotificationService notifications)
        : this(update => new UpdateDialogViewModel(update, configuration), notifications,
            () => CanDownloadAutomatically && configuration.AutoDownload.Value
                && UpdateDialogViewModel.ClaimUpdateFolders())
    {
    }

    internal static bool CanDownloadAutomatically => !FlatpakUpdater.RequiresManualUpdate;

    public IObservable<AppUpdateStatus> Status { get; }

    internal UpdateDialogViewModel? Current => _current.Value;

    public async Task<bool> TryStartAutomaticallyAsync(AppUpdateResponse update)
    {
        // The startup task checks the preference and claims the download folders off the UI thread.
        if (!_downloadsAutomatically()) return false;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_current.Value == null) Start(update);
        });
        return true;
    }

    public void StartInBackground(AppUpdateResponse update)
    {
        // Asking again for an update that is already under way shows how far it has got.
        if (_current.Value != null)
            ShowDialog();
        else
            Start(update);
    }

    public void ShowDialog()
    {
        if (_isDialogOpen || _current.Value is not { } viewModel) return;

        _isDialogOpen = true;
        _ = ShowDialogAsync(viewModel);
    }

    public static async void ScheduleInstallOnQuit(UpdateDialogViewModel viewModel, INotificationService notifications)
    {
        // Leaving a preparing update running needs nothing more.
        if (viewModel.IsFlatpak || viewModel.Phase.Value != AppUpdatePhase.Ready) return;

        if (await viewModel.ScheduleInstallOnQuitAsync())
        {
            notifications.ShowInformation(
                MessageStrings.UpdateAction_UpdateOnQuit,
                MessageStrings.UpdateWillInstallOnQuit);
        }
        else if (!viewModel.CancellationToken.IsCancellationRequested)
        {
            notifications.ShowError(MessageStrings.UpdateFailed_Title, viewModel.ProgressText.Value);
        }
    }

    internal static string GetVersionLabel(string version) => $"Beutl {version}";

    private void Start(AppUpdateResponse update)
    {
        UpdateDialogViewModel viewModel = _createViewModel(update);
        _current.OnNext(viewModel);
        viewModel.Phase.Subscribe(phase => OnPhaseChanged(viewModel, phase));
        viewModel.Start();
    }

    private static CancellationToken UntilPhaseChanges(UpdateDialogViewModel viewModel, AppUpdatePhase phase)
    {
        var announcement = new CancellationTokenSource();
        // A transition before the UI thread shows the notification withdraws it immediately.
        CancellationToken withdrawn = announcement.Token;
        viewModel.Phase.Where(current => current != phase).Take(1)
            .Subscribe(_ =>
            {
                announcement.Cancel();
                announcement.Dispose();
            });
        return withdrawn;
    }

    private static string GetUpdateMessage(AppUpdateResponse update)
        => update.MustLatest
            ? GetVersionLabel(update.LatestVersion) + Environment.NewLine + MessageStrings.UpgradeRequired
            : GetVersionLabel(update.LatestVersion);

    private async Task ShowDialogAsync(UpdateDialogViewModel viewModel)
    {
        try
        {
            await new UpdateDialog
            {
                DataContext = viewModel,
                [Beutl.Controls.ControlNotifications.NotificationsProperty] = _notifications
            }.ShowAsync();
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
            case AppUpdatePhase.Preparing:
                _notifications.Show(new Notification(
                    MessageStrings.UpdateDownloading_Title,
                    GetUpdateMessage(viewModel.Update),
                    Actions: [ReleaseNotesAction(viewModel)])
                {
                    CancellationToken = UntilPhaseChanges(viewModel, phase),
                });
                break;
            case AppUpdatePhase.Ready:
                string message = GetUpdateMessage(viewModel.Update);
                if (viewModel.IncludedExtensions is { } summary)
                    message += Environment.NewLine + summary;
                _notifications.Show(new Notification(
                    MessageStrings.UpdateReady_Title,
                    message,
                    NotificationType.Success,
                    s_readyNotificationDuration,
                    Actions: CreateReadyActions(viewModel))
                {
                    CancellationToken = UntilPhaseChanges(viewModel, phase),
                });
                break;
            case AppUpdatePhase.Failed:
                _notifications.Show(new Notification(
                    MessageStrings.UpdateFailed_Title,
                    viewModel.ProgressText.Value,
                    NotificationType.Error,
                    s_readyNotificationDuration,
                    Actions: [new(MessageStrings.UpdateAction_Retry, viewModel.Retry)])
                {
                    CancellationToken = UntilPhaseChanges(viewModel, phase),
                });
                break;
            case AppUpdatePhase.Completed:
                _notifications.ShowSuccess(
                    GetVersionLabel(viewModel.Update.LatestVersion),
                    viewModel.ProgressText.Value);
                break;
        }
    }

    private NotificationAction[] CreateReadyActions(UpdateDialogViewModel viewModel)
    {
        NotificationAction installNow = new(viewModel.PrimaryButtonText.Value, () => InstallNow(viewModel));
        if (viewModel.IsFlatpak) return [installNow, ReleaseNotesAction(viewModel)];

        NotificationAction onQuit = new(MessageStrings.UpdateAction_UpdateOnQuit, () => ScheduleInstallOnQuit(viewModel, _notifications));
        // The notice of an update that downloaded unasked is gone by now, so the release notes are offered here too.
        return [installNow, onQuit, ReleaseNotesAction(viewModel)];
    }

    // An update without a page of its own links to the list of releases instead. Opening the release notes leaves
    // the notification on screen, with whatever choice it offers.
    private NotificationAction ReleaseNotesAction(UpdateDialogViewModel viewModel)
        => new(MessageStrings.ReleaseNotes, () => OpenUrl(_notifications, viewModel.ReleaseUri?.AbsoluteUri ?? ReleasesPageUrl),
            DismissOnInvoke: false);

    // The dialog stays open through the handoff, so a failure there can be read.
    private void InstallNow(UpdateDialogViewModel viewModel)
    {
        ShowDialog();
        _ = viewModel.HandlePrimaryButtonClick();
    }

    // Opens a page in the browser. False when it did not open; the user is told why.
    internal static bool OpenUrl(INotificationService notifications, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            s_logger.LogError(ex, "Failed to open URL: {Url}", url);
            notifications.ShowError(Strings.Error, ex.Message);
            return false;
        }
    }
}
