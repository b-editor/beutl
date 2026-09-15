using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class MainViewModelShutdownTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task CleanupFailure_AfterCloseWasAccepted_StillClosesWindow(bool mac)
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        Avalonia.Controls.Window window = mac ? new MacWindow() : new MainWindow();
        window.DataContext = viewModel;
        window.Content = null;
        var disposal = typeof(MainViewModel).GetField("_disposeTask",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        // Model the cached task produced when a late disposal step throws.
        disposal.SetValue(viewModel, Task.FromException(new IOException("cleanup failed")));
        try
        {
            window.Show();
            window.Close();
            var closing = (Task)window.GetType().GetField("_viewModelDisposeTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(window.IsVisible, Is.False, "A faulted disposal task must not permanently veto closing.");
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            disposal.SetValue(viewModel, null);
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task CloseVeto_KeepsWindowOpenAndAllowsRetry(bool mac)
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        Avalonia.Controls.Window window = mac ? new MacWindow() : new MainWindow();
        window.DataContext = viewModel;
        window.Content = null;
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "window-veto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "veto", location);
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> veto = (_, _) =>
            Task.FromException(new Beutl.Services.ProjectCloseAbortedException("veto"));
        viewModel.ProjectService.ClosingPreparing += veto;
        try
        {
            window.Show();
            window.Close();
            var closing = (Task)window.GetType().GetField("_viewModelDisposeTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(window.IsVisible, Is.True);
            Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);
            viewModel.ProjectService.ClosingPreparing -= veto;
            window.Close();
            await WaitUntilAsync(() => !window.IsVisible, TimeSpan.FromSeconds(10));
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= veto;
            window.DataContext = null;
            window.Close();
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ClosingRealShell_WaitsForPackageInstallerBeforeHandoff()
    {
        await TestReset.ResetShellAsync();
        var releaseInstaller = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installerWaitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool handedOff = false;
        var viewModel = new MainViewModel(
            _ => handedOff = true,
            _ =>
            {
                installerWaitStarted.TrySetResult();
                return releaseInstaller.Task;
            });

        viewModel.Dispose();
        await installerWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(handedOff, Is.False);

        releaseInstaller.TrySetResult();
        await viewModel.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(handedOff, Is.True);
    }

    [AvaloniaTest]
    public async Task PackageWaitFailureDoesNotSkipShutdownHandoff()
    {
        await TestReset.ResetShellAsync();
        bool handedOff = false;
        var viewModel = new MainViewModel(
            _ => handedOff = true,
            _ => Task.FromException(new InvalidOperationException("wait failed")));
        viewModel.Dispose();
        await viewModel.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(handedOff, Is.True);
    }

    [AvaloniaTest]
    public async Task ClosingRealShell_HandsOffPackageQueueAndDisposesClientsIdempotently()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        PackageChangesQueue? handedOffQueue = null;
        var viewModel = new MainViewModel(clients =>
            handedOffQueue = clients.GetResource<PackageChangesQueue>());
        var window = new MainWindow { DataContext = viewModel };
        // The minimal headless TestApp does not install the production MainView's app-only
        // chrome services. Keep the real MainWindow closing path while avoiding that unrelated
        // visual-tree startup callback.
        window.Content = null;
        viewModel.RegisterExitHandler(lifetime);

        try
        {
            window.Show();
            window.Close();
            await viewModel.WaitForDisposalAsync();
            await WaitUntilAsync(() => !window.IsVisible, TimeSpan.FromSeconds(5));

            Assert.That(handedOffQueue, Is.Not.Null);
            Assert.Throws<ObjectDisposedException>(() =>
                viewModel._beutlClients.GetResource<PackageChangesQueue>());
            Assert.DoesNotThrow(viewModel.Dispose);
            Assert.DoesNotThrow(viewModel.CompleteShutdown);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
            viewModel.Dispose();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ClosingRealShell_WaitsForOpenEditorTabsToDispose()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel, Content = null };
        viewModel.RegisterExitHandler(lifetime);
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-editor");
        Directory.CreateDirectory(workspace);
        Project project = (await viewModel.ProjectService.CreateProject(
            640, 480, 30, 44_100, "shutdown-editor", workspace))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        viewModel.EditorService.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        try
        {
            window.Show();
            window.Close();
            await viewModel.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => !window.IsVisible, TimeSpan.FromSeconds(5));

            Assert.That(viewModel.EditorService.TabItems, Is.Empty);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ShutdownRequest_WithInFlightProjectClose_DrainsBeforeReissuingShutdown()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        int handoffs = 0;
        var viewModel = new MainViewModel(_ => handoffs++);
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "drain", location);
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int closeCalls = 0;
        // Models the version-control close hook that saves and commits before the project closes.
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> gate = async (_, _) =>
        {
            closeCalls++;
            closeStarted.TrySetResult();
            await releaseClose.Task;
        };
        viewModel.ProjectService.ClosingPreparing += gate;
        viewModel.RegisterExitHandler(lifetime);
        ReissueGuard guard = ReissueGuard.Install(lifetime, () => (viewModel.WaitForDisposalAsync().IsCompleted, handoffs));

        try
        {
            Assert.That(lifetime.TryShutdown(), Is.False, "The first request must be refused while the close drains.");
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(guard.Exits, Is.Zero);
            Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);

            // The dispatcher keeps serving callbacks while the close is pending.
            bool pumped = await Dispatcher.UIThread.InvokeAsync(() => true, DispatcherPriority.Input)
                .GetTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(pumped, Is.True);

            Assert.That(lifetime.TryShutdown(), Is.False, "A repeated request joins the in-flight drain.");
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(guard.Reissues, Is.Empty);

            releaseClose.TrySetResult();
            await WaitUntilAsync(() => guard.Reissues.Count > 0, TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();
            viewModel.CompleteShutdown();

            Assert.Multiple(() =>
            {
                Assert.That(guard.Refusals, Is.EqualTo(new[] { true, true, false }), "Two refused requests, then one reissued request that passes.");
                Assert.That(guard.Reissues, Has.Count.EqualTo(1));
                Assert.That(guard.Reissues[0].DisposalComplete, Is.True, "Shutdown must not be reissued before disposal finished.");
                Assert.That(guard.Reissues[0].Handoffs, Is.EqualTo(1));
                Assert.That(handoffs, Is.EqualTo(1), "Exit must not repeat the package handoff.");
                Assert.That(closeCalls, Is.EqualTo(1));
                Assert.That(guard.Exits, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Null);
                Assert.That(viewModel.EditorService.TabItems, Is.Empty);
            });
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= gate;
            releaseClose.TrySetResult();
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ShutdownRequest_WhenCloseIsVetoed_LeavesProjectOpenAndAllowsRetry()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var viewModel = new MainViewModel();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-veto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "veto", location);
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> veto = (_, _) =>
            Task.FromException(new Beutl.Services.ProjectCloseAbortedException("veto"));
        viewModel.ProjectService.ClosingPreparing += veto;
        viewModel.RegisterExitHandler(lifetime);
        ReissueGuard guard = ReissueGuard.Install(lifetime, () => (viewModel.WaitForDisposalAsync().IsCompleted, 0));

        try
        {
            Assert.That(lifetime.TryShutdown(), Is.False);
            await viewModel.WaitForShutdownRequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(guard.Reissues, Is.Empty, "A vetoed close must not reissue the shutdown.");
                Assert.That(guard.Exits, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);
                Assert.That(viewModel.WaitForDisposalAsync().IsCompleted, Is.True);
            });

            viewModel.ProjectService.ClosingPreparing -= veto;
            Assert.That(lifetime.TryShutdown(), Is.False, "The retry drains asynchronously as well.");
            await WaitUntilAsync(() => guard.Reissues.Count > 0, TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(guard.Refusals, Is.EqualTo(new[] { true, true, false }));
                Assert.That(guard.Reissues[0].DisposalComplete, Is.True);
                Assert.That(guard.Exits, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Null);
            });
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= veto;
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task WindowCloseAndShutdownRequest_ShareOneProjectClose()
    {
        await TestReset.ResetShellAsync();
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        int handoffs = 0;
        var viewModel = new MainViewModel(_ => handoffs++);
        var window = new MainWindow { DataContext = viewModel, Content = null };
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "window", location);
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int closeCalls = 0;
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> gate = async (_, _) =>
        {
            closeCalls++;
            closeStarted.TrySetResult();
            await releaseClose.Task;
        };
        viewModel.ProjectService.ClosingPreparing += gate;
        viewModel.RegisterExitHandler(lifetime);
        ReissueGuard guard = ReissueGuard.Install(lifetime, () => (viewModel.WaitForDisposalAsync().IsCompleted, handoffs));

        try
        {
            window.Show();
            window.Close();
            Assert.That(lifetime.TryShutdown(), Is.False);
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(window.IsVisible, Is.True);

            releaseClose.TrySetResult();
            await WaitUntilAsync(() => guard.Reissues.Count > 0, TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => !window.IsVisible, TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(closeCalls, Is.EqualTo(1), "The window and the lifetime must share one close.");
                Assert.That(guard.Reissues, Has.Count.EqualTo(1));
                Assert.That(guard.Reissues[0].DisposalComplete, Is.True);
                Assert.That(handoffs, Is.EqualTo(1));
                Assert.That(guard.Exits, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Null);
            });
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= gate;
            releaseClose.TrySetResult();
            window.DataContext = null;
            if (window.IsVisible)
            {
                window.Close();
            }
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ForcedExit_DuringInFlightDisposal_DrainsDisposalBeforeReturning()
    {
        await TestReset.ResetShellAsync();
        bool handedOff = false;
        var releaseInstaller = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new MainViewModel(
            _ => handedOff = true,
            _ =>
            {
                // Only the dispatcher pump inside the Exit fallback can run this job.
                Dispatcher.UIThread.Post(() => releaseInstaller.TrySetResult(), DispatcherPriority.Background);
                return releaseInstaller.Task;
            });

        try
        {
            viewModel.Dispose();
            Assert.That(viewModel.WaitForDisposalAsync().IsCompleted, Is.False, "Disposal must still be pending when Exit arrives.");

            viewModel.CompleteShutdown();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.WaitForDisposalAsync().IsCompleted, Is.True, "Exit must not return with disposal still running.");
                Assert.That(handedOff, Is.True);
            });
        }
        finally
        {
            releaseInstaller.TrySetResult();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    [AvaloniaTest]
    public async Task ForcedExit_DuringInFlightAsyncClose_JoinsTheSharedCloseWithoutPromptingAgain()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-join-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "join", location);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int closeCalls = 0;
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> vetoAfterRelease = async (_, _) =>
        {
            closeCalls++;
            await releaseClose.Task;
            throw new Beutl.Services.ProjectCloseAbortedException("veto");
        };
        viewModel.ProjectService.ClosingPreparing += vetoAfterRelease;

        try
        {
            Task<bool> windowClose = viewModel.TryDisposeForWindowCloseAsync();
            Assert.That(windowClose.IsCompleted, Is.False);
            Assert.That(closeCalls, Is.EqualTo(1));
            Dispatcher.UIThread.Post(() => releaseClose.TrySetResult(), DispatcherPriority.Background);

            bool disposed = viewModel.TryDisposeForWindowClose();
            await windowClose;

            Assert.Multiple(() =>
            {
                Assert.That(disposed, Is.False, "A refused in-flight close must refuse the forced exit too.");
                Assert.That(windowClose.Result, Is.False);
                Assert.That(closeCalls, Is.EqualTo(1), "The forced exit must join the in-flight close instead of prompting again.");
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);
                Assert.That(viewModel.WaitForDisposalAsync().IsCompleted, Is.True);
            });
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= vetoAfterRelease;
            releaseClose.TrySetResult();
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
            viewModel.CompleteShutdown();
        }
    }

    /// <summary>
    /// Observes the real lifetime's requests after <see cref="MainViewModel"/> has answered them.
    /// A request the view model lets through is the reissued shutdown; the guard records it and
    /// then refuses it itself, because a request that reaches Exit shuts down the headless
    /// dispatcher shared by every test in this assembly.
    /// </summary>
    private sealed class ReissueGuard
    {
        private readonly Func<(bool DisposalComplete, int Handoffs)> _snapshot;

        private ReissueGuard(Func<(bool DisposalComplete, int Handoffs)> snapshot)
        {
            _snapshot = snapshot;
        }

        public List<bool> Refusals { get; } = [];

        public List<(bool DisposalComplete, int Handoffs)> Reissues { get; } = [];

        public int Exits { get; private set; }

        public static ReissueGuard Install(
            ClassicDesktopStyleApplicationLifetime lifetime,
            Func<(bool DisposalComplete, int Handoffs)> snapshot)
        {
            var guard = new ReissueGuard(snapshot);
            lifetime.ShutdownRequested += guard.OnShutdownRequested;
            lifetime.Exit += (_, _) => guard.Exits++;
            return guard;
        }

        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            Refusals.Add(e.Cancel);
            if (e.Cancel)
                return;

            Reissues.Add(_snapshot());
            e.Cancel = true;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The shell did not finish closing.");
            await Task.Delay(10);
        }
    }

}
