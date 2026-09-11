using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.NUnit;
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
