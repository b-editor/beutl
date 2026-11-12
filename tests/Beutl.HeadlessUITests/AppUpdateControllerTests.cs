using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.Utilities;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

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

    private AppUpdateResponse Update(string? url = null) => new()
    {
        LatestVersion = "2.0.0",
        Url = url,
        DownloadUrl = "https://downloads.example.test/" + _fileName,
        IsLatest = false,
        MustLatest = false,
    };

    // The installer type needs no unpacking, so it is ready as soon as it has downloaded.
    private static UpdateDialogViewModel ViewModel(AppUpdateResponse update, HttpMessageHandler handler)
        => new(update, false, httpClient: new HttpClient(handler), loadMetadata: () => Task.FromResult<AssetMetadataJson?>(new AssetMetadataJson
        {
            Id = "test",
            OS = "test",
            Arch = "test",
            Version = "2.0.0",
            Standalone = "false",
            Type = "installer",
        }));

    [AvaloniaTest]
    public async Task AReadyUpdateOffersToInstallNowOrOnQuit()
    {
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(update => vm = ViewModel(update, new Handler(HttpStatusCode.OK)));

        controller.StartInBackground(Update());
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        // Starting the download says nothing, so the only notification is the one that asks for a decision.
        Notification ready = _shown.Single();
        Assert.Multiple(() =>
        {
            Assert.That(ready.Title, Is.EqualTo(MessageStrings.UpdateReady_Title));
            Assert.That(ready.Message, Is.EqualTo("Beutl 2.0.0"));
            Assert.That(ready.Actions!.Select(action => action.Text), Is.EqualTo(new[]
            {
                MessageStrings.UpdateAction_StartInstaller, MessageStrings.UpdateAction_UpdateOnQuit,
            }));
        });
    }

    // An update found at startup downloads without asking when the settings allow it. Downloading asks for nothing, so
    // its notice is as brief as any other and gives way to the notification that asks for a decision.
    [AvaloniaTest]
    public async Task AnUpdateFoundAtStartupDownloadsUnaskedWithABriefNotice()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateDialogViewModel? vm = null;
        var controller = new AppUpdateController(
            update => vm = ViewModel(update, new Handler(release.Task, HttpStatusCode.OK)),
            downloadsAutomatically: () => true);

        Assert.That(controller.TryStartAutomatically(Update("https://example.test/releases/2.0.0")), Is.True);
        Notification downloading = _shown.Single();
        Assert.Multiple(() =>
        {
            Assert.That(downloading.Title, Is.EqualTo(MessageStrings.UpdateDownloading_Title));
            Assert.That(downloading.Message, Is.EqualTo("Beutl 2.0.0"));
            Assert.That(downloading.Type, Is.EqualTo(NotificationType.Information));
            Assert.That(downloading.Expiration, Is.Null);
            Assert.That(downloading.Actions!.Select(action => action.Text), Is.EqualTo(new[] { MessageStrings.ReleaseNotes }));
            Assert.That(downloading.CancellationToken.IsCancellationRequested, Is.False);
        });

        release.SetResult();
        await vm!.UpdateTask!.WaitAsync(TimeSpan.FromSeconds(5));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(downloading.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(_shown, Has.Count.EqualTo(2));
            Assert.That(_shown[^1].Title, Is.EqualTo(MessageStrings.UpdateReady_Title));
        });
    }

    // With automatic downloads turned off, or where they cannot run, the update waits for the user.
    [Test]
    public void AnUpdateFoundAtStartupWaitsForTheUserWhenDownloadsAreNotAutomatic()
    {
        var controller = new AppUpdateController(
            _ => throw new AssertionException("The update started without being asked."),
            downloadsAutomatically: () => false);

        Assert.Multiple(() =>
        {
            Assert.That(controller.TryStartAutomatically(Update()), Is.False);
            Assert.That(controller.Current, Is.Null);
            Assert.That(_shown, Is.Empty);
        });
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

        NotificationAction[] actions = [.. _shown.Single().Actions!];
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

        Assert.That(_shown.Single().Title, Is.EqualTo(MessageStrings.UpdateReady_Title));
        Assert.That(onUIThread, Is.EqualTo(new[] { true }));
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await release.WaitAsync(token);
            HttpStatusCode code = codes[Math.Min(_next++, codes.Length - 1)];
            return new HttpResponseMessage(code) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        }
    }
}
