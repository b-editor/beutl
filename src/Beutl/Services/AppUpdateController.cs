using System.Reactive.Subjects;
using Avalonia.Threading;
using Beutl.Api.Clients;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
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
    private readonly Func<AppUpdateResponse, UpdateDialogViewModel> _createViewModel;
    private readonly BehaviorSubject<UpdateDialogViewModel?> _current = new(null);
    private bool _isDialogOpen;

    internal AppUpdateController(Func<AppUpdateResponse, UpdateDialogViewModel> createViewModel)
    {
        _createViewModel = createViewModel;
        Status = _current
            .Select(viewModel => viewModel == null
                ? Observable.Return(AppUpdateStatus.Idle)
                : viewModel.Phase.CombineLatest(viewModel.ProgressText, viewModel.DownloadProgress,
                    (phase, text, progress) => new AppUpdateStatus(text ?? "", phase == AppUpdatePhase.Preparing, progress)))
            .Switch();
    }

    public static AppUpdateController Instance { get; } = new(update => new UpdateDialogViewModel(update));

    public IObservable<AppUpdateStatus> Status { get; }

    internal UpdateDialogViewModel? Current => _current.Value;

    public void StartInBackground(AppUpdateResponse update)
    {
        // Asking again for an update that is already under way shows how far it has got.
        if (!Start(update))
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

    private bool Start(AppUpdateResponse update, Action? beforeStart = null)
    {
        if (_current.Value != null)
        {
            beforeStart?.Invoke();
            return false;
        }

        UpdateDialogViewModel viewModel = _createViewModel(update);
        viewModel.Phase.Skip(1).Subscribe(phase => OnPhaseChanged(viewModel, phase));
        _current.OnNext(viewModel);
        beforeStart?.Invoke();
        viewModel.Start();
        return true;
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
                    Actions:
                    [
                        new(viewModel.PrimaryButtonText.Value, () => InstallNow(viewModel)),
                        new(MessageStrings.UpdateAction_UpdateOnQuit, () => ScheduleInstallOnQuit(viewModel)),
                    ])
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

    // The dialog stays open through the handoff, so a failure there can be read.
    private void InstallNow(UpdateDialogViewModel viewModel)
    {
        ShowDialog();
        _ = viewModel.HandlePrimaryButtonClick();
    }
}
