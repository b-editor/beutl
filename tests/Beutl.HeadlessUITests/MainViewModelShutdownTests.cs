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
        await viewModel.EditorService.ActivateTabItemAsync(scene);
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

    [AvaloniaTest]
    public async Task SynchronousAndAsynchronousDisposalJoinThePublishedTerminalTask()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();

        viewModel.Dispose();
        Task first = viewModel.WaitForDisposalAsync();
        Task second = viewModel.DisposeAsync().AsTask();

        Assert.That(second, Is.SameAs(first));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void DisposalAttemptsEveryOwnedComponentAndAggregatesFailures()
    {
        var attempted = new List<string>();

        AggregateException? failure = Assert.ThrowsAsync<AggregateException>(async () =>
            await MainViewModel.DisposeComponentsAsync(
                () =>
                {
                    attempted.Add("palette");
                    throw new InvalidOperationException("palette");
                },
                () => attempted.Add("agent"),
                () =>
                {
                    attempted.Add("project");
                    return Task.FromException(new InvalidOperationException("project"));
                },
                () =>
                {
                    attempted.Add("editor");
                    return ValueTask.FromException(new InvalidOperationException("editor"));
                },
                () => attempted.Add("items")));

        Assert.Multiple(() =>
        {
            Assert.That(attempted, Is.EqualTo(new[] { "palette", "agent", "project", "editor", "items" }));
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(3));
        });
    }
}
