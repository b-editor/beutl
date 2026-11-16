using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Beutl.Utilities;
using Beutl.ViewModels.Dialogs;
using Beutl.Views;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// An update prepares behind the editor and comes back with what the user can do next.
[TestFixture, NonParallelizable]
public sealed class AppUpdateControllerTests
{
    private readonly List<Notification> _shown = [];
    private INotificationServiceHandler? _previous;
    private string _fileName = null!;

    // onUIThread, when given, records for each notification whether it was shown from the UI thread.
    private sealed class RecordingHandler(List<Notification> shown, List<bool>? onUIThread = null) : INotificationServiceHandler
    {
        public void Show(Notification notification)
        {
            onUIThread?.Add(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            shown.Add(notification);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _shown.Clear();
        _previous = NotificationService.Handler;
        NotificationService.Handler = new RecordingHandler(_shown);
        _fileName = "beutl-test-update-" + Guid.NewGuid().ToString("N") + ".bin";
    }

    [TearDown]
    public void TearDown()
    {
        if (_previous != null) NotificationService.Handler = _previous;
        string file = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "download", _fileName);
        if (File.Exists(file)) File.Delete(file);
    }

    private AppUpdateResponse Update(string? url = null, bool mustLatest = false, bool hasDownload = true) => new()
    {
        LatestVersion = "2.0.0",
        Url = url,
        DownloadUrl = hasDownload ? "https://downloads.example.test/" + _fileName : null,
        IsLatest = false,
        MustLatest = mustLatest,
    };

    // The installer type needs no unpacking, so it is ready as soon as it has downloaded.
    private static UpdateDialogViewModel ViewModel(
        AppUpdateResponse update, HttpMessageHandler handler, string type = "installer", Func<bool>? claimUpdateFolders = null)
        => new(update, false, httpClient: new HttpClient(handler), loadMetadata: () => Task.FromResult<AssetMetadataJson?>(new AssetMetadataJson
        {
            Id = "test",
            OS = "test",
            Arch = "test",
            Version = "2.0.0",
            Standalone = "false",
            Type = type,
        }), claimUpdateFolders: claimUpdateFolders);

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task WithAutoDownloadOffTheUserCanDownloadAndThenChooseWhenToInstall(bool mustLatest)
    {
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update => vm = ViewModel(update, new Handler(HttpStatusCode.OK)));

        await CheckForUpdatesTask.HandleUpdateCheckAsync(null, Update(mustLatest: mustLatest), controller);
        Assert.That(vm, Is.Null);
        Notification available = _shown.Single();
        Assert.That(available.Title, Is.EqualTo(mustLatest ? MessageStrings.UpgradeRequired : MessageStrings.NewVersionAvailable));
        available.Actions!.Single(action => action.Text == MessageStrings.UpdateAction_Download).Callback();
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Notification ready = _shown[^1];
        Assert.Multiple(() =>
        {
            Assert.That(ready.Title, Is.EqualTo(MessageStrings.UpdateReady_Title));
            Assert.That(ready.Actions!.Select(action => action.Text), Is.EqualTo(new[]
            {
                MessageStrings.UpdateAction_StartInstaller, MessageStrings.UpdateAction_UpdateOnQuit, MessageStrings.ReleaseNotes,
            }));
        });
    }

    // An update found at startup downloads without asking when the settings allow it. Downloading asks for nothing, so
    // its notice is as brief as any other and gives way to the notification that asks for a decision.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task AnUpdateFoundAtStartupDownloadsUnaskedWithABriefNotice(bool mustLatest)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(
            update => vm = ViewModel(update, new Handler(release.Task, HttpStatusCode.OK)),
            downloadsAutomatically: () => true);

        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Width = 1000, Height = 600 };
        try
        {
            window.Show();
            window.Content = view;
            NotificationService.Handler = new NotificationServiceHandler(() => view);
            await CheckForUpdatesTask.HandleUpdateCheckAsync(null, Update(mustLatest: mustLatest), controller)
                .WaitAsync(TimeSpan.FromSeconds(5));

            async Task<Notification> VisibleNotice(string title)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                {
                    HeadlessTestHelpers.Render();
                    return view.NotificationPanel.Children is [FAInfoBar { Opacity: > 0.99 } bar] && bar.Title == title;
                });
                return (Notification)view.NotificationPanel.Children.Single().DataContext!;
            }

            Notification downloading = await VisibleNotice(MessageStrings.UpdateDownloading_Title);
            HeadlessCapture.SaveIfRequested(view, nameof(AppUpdateControllerTests), $"downloading-required-{mustLatest}");
            string message = mustLatest ? $"Beutl 2.0.0{Environment.NewLine}{MessageStrings.UpgradeRequired}" : "Beutl 2.0.0";
            Assert.Multiple(() =>
            {
                Assert.That(downloading.Message, Is.EqualTo(message));
                Assert.That(downloading.Type, Is.EqualTo(NotificationType.Information));
                Assert.That(downloading.Expiration, Is.Null);
                Assert.That(downloading.Actions!.Select(action => action.Text), Is.EqualTo(new[] { MessageStrings.ReleaseNotes }));
                Assert.That(downloading.CancellationToken.IsCancellationRequested, Is.False);
            });

            release.SetResult();
            await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Notification ready = await VisibleNotice(MessageStrings.UpdateReady_Title);
            Assert.That(ready.Message, Is.EqualTo(message));
            Assert.That(downloading.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(view.HiddenNotificationPanel.Children, Is.Empty);
            HeadlessCapture.SaveIfRequested(view, nameof(AppUpdateControllerTests), $"ready-required-{mustLatest}");
        }
        finally
        {
            vm?.Cancel();
            release.TrySetResult();
            if (vm?.UpdateTask is { } task) await task;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task AReleaseWithoutADownloadOffersItsReleasePage(bool legacy)
    {
        var controller = new AppUpdateController(
            _ => throw new AssertionException("There is no file to download."), downloadsAutomatically: () => true);
        AppUpdateResponse update = Update("https://example.test/releases/2.0.0", hasDownload: false);
        var v1 = new CheckForUpdatesResponse { LatestVersion = update.LatestVersion, Url = update.Url!, IsLatest = false, MustLatest = false };

        await CheckForUpdatesTask.HandleUpdateCheckAsync(legacy ? v1 : null, legacy ? null : update, controller);
        Notification notice = _shown.Single();
        Assert.That(notice.Title, Is.EqualTo(MessageStrings.NewVersionAvailable));
        Assert.That(notice.Actions!.Select(action => action.Text), Is.EqualTo(new[] { MessageStrings.ReleaseNotes }));
    }

    // The notice of an update that downloaded unasked is gone once the update is ready, so the release notes are offered
    // again there. Opening them leaves the choice on screen.
    [AvaloniaTest]
    public async Task TheReadyNotificationOffersTheReleaseNotesAndStays()
    {
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update => vm = ViewModel(update, new Handler(HttpStatusCode.OK)));

        controller.StartInBackground(Update("https://example.test/releases/2.0.0"));
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        NotificationAction[] actions = [.. _shown[^1].Actions!];
        Assert.Multiple(() =>
        {
            Assert.That(actions.Select(action => action.Text), Is.EqualTo(new[]
            {
                MessageStrings.UpdateAction_StartInstaller, MessageStrings.UpdateAction_UpdateOnQuit, MessageStrings.ReleaseNotes,
            }));
            Assert.That(actions[^1].DismissOnInvoke, Is.False);
        });
    }

    // The update prepares on the thread pool, while its dialog opens and closes on the UI thread. Whether the outcome
    // is notified depends on the dialog, so it is decided on the UI thread, in order with the dialog.
    [AvaloniaTest]
    public async Task AnUpdateThatBecomesReadyBehindTheEditorIsDecidedOnTheUIThread()
    {
        var onUIThread = new List<bool>();
        NotificationService.Handler = new RecordingHandler(_shown, onUIThread);
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update => vm = ViewModel(update, new Handler(HttpStatusCode.OK)));

        controller.StartInBackground(Update());
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Assert.That(_shown.Select(n => n.Title), Is.EqualTo(new[] { MessageStrings.UpdateDownloading_Title, MessageStrings.UpdateReady_Title }));
        Assert.That(onUIThread, Is.EqualTo(new[] { true, true }));
    }

    [AvaloniaTest]
    public async Task TheStatusCarriesTheDownloadProgressOnlyWhileItRuns()
    {
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update => vm = ViewModel(update, new Handler(HttpStatusCode.OK)));
        var progress = new List<double?>();
        using IDisposable subscription = controller.Status.Subscribe(status => progress.Add(status.Progress));

        controller.StartInBackground(Update());
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(progress, Does.Contain((double?)1.0));
        Assert.That(progress[^1], Is.Null);
    }

    [AvaloniaTest]
    public async Task AFailedUpdateNotifiesAndItsRetryReachesReady()
    {
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update =>
            vm = ViewModel(update, new Handler(HttpStatusCode.InternalServerError, HttpStatusCode.OK)));

        controller.StartInBackground(Update());
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Notification failed = _shown[^1];
        Assert.That(failed.Type, Is.EqualTo(NotificationType.Error));
        Assert.That(failed.Message, Does.StartWith(MessageStrings.DownloadFailed));

        failed.Actions!.Single().Callback();
        await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
        Assert.That(_shown[^1].Title, Is.EqualTo(MessageStrings.UpdateReady_Title));
    }

    [AvaloniaTest]
    public async Task ACanceledUpdateWithdrawsWhatOffersItAndCanStartAgain()
    {
        var created = new List<UpdateDialogViewModel>();
        var controller = new AppUpdateController(update =>
        {
            var vm = ViewModel(update, new Handler(HttpStatusCode.OK));
            created.Add(vm);
            return vm;
        });
        AppUpdateStatus? status = null;
        using IDisposable subscription = controller.Status.Subscribe(value => status = value);

        controller.StartInBackground(Update());
        await created[0].UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();
        Assert.That(status!.IsActive, Is.True);
        CancellationToken offer = _shown[^1].CancellationToken;

        created[0].Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(offer.IsCancellationRequested, Is.True);
            Assert.That(controller.Current, Is.Null);
            Assert.That(status!.IsActive, Is.False);
        });

        controller.StartInBackground(Update());
        await created[1].UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(created[1].Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
    }

    [AvaloniaTest]
    public async Task RunInBackground_ClosesTheDialogAndLeavesTheDownloadRunning()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = ViewModel(Update(), new Handler(release.Task, HttpStatusCode.OK));
        var dialog = new UpdateDialog { DataContext = vm };
        var window = new Window { Width = 800, Height = 500 };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        vm.Start();
        try
        {
            HeadlessTestHelpers.Render();
            Button secondary = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SecondaryButton");
            Assert.That(secondary.Content, Is.EqualTo(MessageStrings.UpdateAction_RunInBackground));

            secondary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(vm.CancellationToken.IsCancellationRequested, Is.False);
            release.SetResult();
            await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
        }
        finally
        {
            release.TrySetResult();
            dialog.Hide();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task AMetadataFailureOffersRetry()
    {
        using var client = new HttpClient(new Handler(HttpStatusCode.OK));
        var vm = new UpdateDialogViewModel(Update(), false, httpClient: client,
            loadMetadata: () => throw new IOException("metadata is unreadable"));

        vm.Start();
        await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Failed));
            Assert.That(vm.IsRetryVisible.Value, Is.True);
            Assert.That(vm.ProgressText.Value, Does.Contain("metadata is unreadable"));
        });
    }

    // The metadata says whether the update unpacks, so it is read before anything downloads.
    [AvaloniaTest]
    public async Task MissingMetadataFailsBeforeDownloading()
    {
        var handler = new Handler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var vm = new UpdateDialogViewModel(Update(), false, httpClient: client,
            loadMetadata: () => Task.FromResult<AssetMetadataJson?>(null));

        vm.Start();
        await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Failed));
            Assert.That(vm.IsRetryVisible.Value, Is.True);
            Assert.That(vm.ProgressText.Value, Is.EqualTo(MessageStrings.FailedToLoadMetadata));
            Assert.That(handler.Requests, Is.Zero, "nothing downloads");
        });
    }

    // A macOS app unpacks into folders that every running Beutl shares and empties first, so while another Beutl holds
    // them, where its own update may be ready, the update stops before downloading and says why. Once that Beutl has
    // quit, a retry goes ahead.
    [AvaloniaTest]
    public async Task AnUpdateThatUnpacksLeavesTheFoldersAnotherBeutlHoldsAlone()
    {
        bool claimed = false;
        var handler = new Handler(HttpStatusCode.NotFound);
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update =>
            vm = ViewModel(update, handler, type: "app", claimUpdateFolders: () => claimed));

        controller.StartInBackground(Update());
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Failed));
            Assert.That(vm.ProgressText.Value, Is.EqualTo(MessageStrings.UpdateFoldersInUse));
            Assert.That(_shown[^1].Message, Is.EqualTo(MessageStrings.UpdateFoldersInUse));
            Assert.That(handler.Requests, Is.Zero, "nothing downloads");
        });

        claimed = true;
        _shown[^1].Actions!.Single().Callback();
        await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(handler.Requests, Is.EqualTo(1), "the retry downloads");
    }

    // An installer unpacks nothing, so it prepares whichever Beutl holds the folders.
    [AvaloniaTest]
    public async Task AnInstallerNeedsNoClaimOnTheFolders()
    {
        int asked = 0;
        UpdateDialogViewModel vm = ViewModel(Update(), new Handler(HttpStatusCode.OK), claimUpdateFolders: () =>
        {
            asked++;
            return false;
        });

        vm.Start();
        await vm.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Phase.Value, Is.EqualTo(AppUpdatePhase.Ready));
            Assert.That(asked, Is.Zero);
        });
    }

    [Test]
    public void DownloadProgressShowsThePercentageAndTheSizes()
    {
        string text = UpdateDialogViewModel.FormatDownloadProgress(512, 1024);

        Assert.That(text, Is.EqualTo(
            $"{MessageStrings.Downloading} 50% ({StringFormats.ToHumanReadableSize(512)} / {StringFormats.ToHumanReadableSize(1024)})"));
    }

    private sealed class Handler(Task release, params HttpStatusCode[] codes) : HttpMessageHandler
    {
        private int _next;

        public Handler(params HttpStatusCode[] codes) : this(Task.CompletedTask, codes)
        {
        }

        public int Requests => _next;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await release.WaitAsync(token);
            HttpStatusCode code = codes[Math.Min(_next++, codes.Length - 1)];
            return new HttpResponseMessage(code) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        }
    }
}
