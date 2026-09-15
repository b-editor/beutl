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
        // The real lifetime stops the shared headless dispatcher once a request reaches Exit,
        // so the reissued request is recorded instead of being forwarded to TryShutdown.
        using var lifetime = new ClassicDesktopStyleApplicationLifetime();
        int handoffs = 0;
        var reissues = new List<(bool DisposalComplete, int Handoffs)>();
        MainViewModel? viewModel = null;
        viewModel = new MainViewModel(
            _ => handoffs++,
            requestShutdown: _ =>
            {
                reissues.Add((viewModel!.WaitForDisposalAsync().IsCompleted, handoffs));
                return true;
            });
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
        var refusals = new List<bool>();
        // Subscribed after the view model so it observes the answer the lifetime will act on.
        lifetime.ShutdownRequested += (_, e) => refusals.Add(e.Cancel);
        int exits = 0;
        lifetime.Exit += (_, _) => exits++;

        try
        {
            Assert.That(lifetime.TryShutdown(), Is.False, "The first request must be refused while the close drains.");
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(exits, Is.Zero);
            Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);

            // The dispatcher keeps serving callbacks while the close is pending.
            bool pumped = await Dispatcher.UIThread.InvokeAsync(() => true, DispatcherPriority.Input)
                .GetTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(pumped, Is.True);

            Assert.That(lifetime.TryShutdown(), Is.False, "A repeated request joins the in-flight drain.");
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(reissues, Is.Empty);

            releaseClose.TrySetResult();
            await WaitUntilAsync(() => reissues.Count > 0, TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            // The reissued request is the only thing left between the lifetime and Exit.
            var reissued = new ShutdownRequestedEventArgs();
            viewModel.OnShutdownRequested(lifetime, reissued);
            viewModel.CompleteShutdown();

            Assert.Multiple(() =>
            {
                Assert.That(refusals, Is.EqualTo(new[] { true, true }));
                Assert.That(reissues, Has.Count.EqualTo(1));
                Assert.That(reissues[0].DisposalComplete, Is.True, "Shutdown must not be reissued before disposal finished.");
                Assert.That(reissues[0].Handoffs, Is.EqualTo(1));
                Assert.That(reissued.Cancel, Is.False, "The reissued request must reach Exit.");
                Assert.That(handoffs, Is.EqualTo(1), "Exit must not repeat the package handoff.");
                Assert.That(closeCalls, Is.EqualTo(1));
                Assert.That(exits, Is.Zero);
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
        int reissues = 0;
        var viewModel = new MainViewModel(null, requestShutdown: _ =>
        {
            reissues++;
            return true;
        });
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, "shutdown-veto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        await viewModel.ProjectService.CreateProject(640, 480, 30, 44100, "veto", location);
        Func<Beutl.Services.ProjectService.ProjectCloseContext, CancellationToken, Task> veto = (_, _) =>
            Task.FromException(new Beutl.Services.ProjectCloseAbortedException("veto"));
        viewModel.ProjectService.ClosingPreparing += veto;
        viewModel.RegisterExitHandler(lifetime);
        int exits = 0;
        lifetime.Exit += (_, _) => exits++;

        try
        {
            Assert.That(lifetime.TryShutdown(), Is.False);
            await viewModel.WaitForShutdownRequestAsync().WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(reissues, Is.Zero, "A vetoed close must not reissue the shutdown.");
                Assert.That(exits, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);
                Assert.That(viewModel.WaitForDisposalAsync().IsCompleted, Is.True);
            });

            viewModel.ProjectService.ClosingPreparing -= veto;
            Assert.That(lifetime.TryShutdown(), Is.False, "The retry drains asynchronously as well.");
            await WaitUntilAsync(() => reissues > 0, TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(reissues, Is.EqualTo(1));
                Assert.That(exits, Is.Zero);
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
        int reissues = 0;
        var viewModel = new MainViewModel(_ => handoffs++, requestShutdown: _ =>
        {
            reissues++;
            return true;
        });
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

        try
        {
            window.Show();
            window.Close();
            Assert.That(lifetime.TryShutdown(), Is.False);
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(window.IsVisible, Is.True);

            releaseClose.TrySetResult();
            await WaitUntilAsync(() => reissues > 0, TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => !window.IsVisible, TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(closeCalls, Is.EqualTo(1), "The window and the lifetime must share one close.");
                Assert.That(reissues, Is.EqualTo(1));
                Assert.That(handoffs, Is.EqualTo(1));
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
