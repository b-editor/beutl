using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class ShutdownPipelineTests
{
    [AvaloniaTest]
    public async Task Coordinator_coalesces_repeated_close_attempts_and_keeps_dispatcher_live()
    {
        int cleanupCalls = 0;
        int closeCalls = 0;
        bool closeRanOnDispatcher = false;
        var dispatcherWorkCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new WindowShutdownCoordinator(
            async cancellationToken =>
            {
                cleanupCalls++;
                Dispatcher.UIThread.Post(dispatcherWorkCompleted.SetResult);
                await dispatcherWorkCompleted.Task.WaitAsync(cancellationToken);
            },
            () =>
            {
                closeCalls++;
                closeRanOnDispatcher = Dispatcher.UIThread.CheckAccess();
            });

        Task first = coordinator.BeginShutdownAsync();
        Task second = coordinator.BeginShutdownAsync();

        Assert.That(second, Is.SameAs(first));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.BeginShutdownAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cleanupCalls, Is.EqualTo(1));
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(closeRanOnDispatcher, Is.True);
            Assert.That(coordinator.CanClose, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Coordinator_cancels_cleanup_and_closes_when_deadline_expires()
    {
        int closeCalls = 0;
        var cancellationObserved = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new WindowShutdownCoordinator(
            async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    cancellationObserved.TrySetResult(cancellationToken.IsCancellationRequested);
                }
            },
            () => closeCalls++,
            TimeSpan.FromMilliseconds(25));

        await coordinator.BeginShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        bool wasCancelled = await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(wasCancelled, Is.True);
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(coordinator.CanClose, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Coordinator_observes_cleanup_faults_after_the_deadline()
    {
        int closeCalls = 0;
        var cleanup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new WindowShutdownCoordinator(
            _ => cleanup.Task,
            () => closeCalls++,
            TimeSpan.FromMilliseconds(25));

        await coordinator.BeginShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Task? observation = coordinator.LateCompletionObservation;
        cleanup.TrySetException(new InvalidOperationException("late cleanup failure"));
        await observation!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(observation, Is.Not.Null);
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(coordinator.CanClose, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Coordinator_closes_once_when_cleanup_throws()
    {
        int closeCalls = 0;
        var coordinator = new WindowShutdownCoordinator(
            _ => Task.FromException(new InvalidOperationException("cleanup failed")),
            () => closeCalls++);

        await coordinator.BeginShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.BeginShutdownAsync();

        Assert.Multiple(() =>
        {
            Assert.That(closeCalls, Is.EqualTo(1));
            Assert.That(coordinator.CanClose, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Shutdown_waits_for_version_control_transition_then_performs_final_close()
    {
        await TestReset.ResetShellAsync();
        var projectService = new ProjectService();
        Project project = SetOpenProject("shutdown-transition");
        var closingPurposes = new List<ProjectTransitionPurpose>();
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing = (_, _) =>
        {
            closingPurposes.Add(projectService.CurrentTransition!.Purpose);
            return Task.CompletedTask;
        };
        projectService.Closing += closing;
        ProjectService.ProjectTransitionScope mutation =
            await projectService.BeginVersionControlTransitionAsync(this, CancellationToken.None);

        try
        {
            await mutation.CloseProjectAsync();
            Task shutdown = FinalCloseAsync();

            Assert.That(shutdown.IsCompleted, Is.False);
            BeutlApplication.Current.Project = project;
            await mutation.DisposeAsync();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(
                    closingPurposes,
                    Is.EqualTo(new[]
                    {
                        ProjectTransitionPurpose.VersionControlMutation,
                        ProjectTransitionPurpose.Shutdown,
                    }));
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await using ProjectService.ProjectTransitionScope rejected =
                        await projectService.BeginVersionControlTransitionAsync(
                            this,
                            CancellationToken.None);
                });
            });
        }
        finally
        {
            await mutation.DisposeAsync();
            projectService.Closing -= closing;
            projectService.CloseProjectImmediately();
        }

        async Task FinalCloseAsync()
        {
            await using ProjectService.ProjectTransitionScope shutdown =
                await projectService.BeginShutdownTransitionAsync(this);
            await shutdown.CloseProjectAsync();
        }
    }

    [AvaloniaTest]
    public async Task Project_close_awaits_all_completion_callbacks_and_isolates_failures()
    {
        await TestReset.ResetShellAsync();
        var projectService = new ProjectService();
        SetOpenProject("async-close-completions");
        var firstCompletionEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool? firstObservedClosed = null;
        bool? secondObservedClosed = null;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (closeContext, _) =>
            {
                closeContext.RegisterCompletion(async projectClosed =>
                {
                    firstCompletionEntered.TrySetResult();
                    await releaseFirstCompletion.Task;
                    firstObservedClosed = projectClosed;
                    throw new InvalidOperationException("Expected completion failure.");
                });
                closeContext.RegisterCompletion(projectClosed =>
                {
                    secondObservedClosed = projectClosed;
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            };
        projectService.Closing += closing;

        try
        {
            Task close = projectService.CloseProject();
            await firstCompletionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(close.IsCompleted, Is.False);

            releaseFirstCompletion.TrySetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(firstObservedClosed, Is.True);
                Assert.That(secondObservedClosed, Is.True);
                Assert.That(projectService.CurrentProject.Value, Is.Null);
            });
        }
        finally
        {
            releaseFirstCompletion.TrySetResult();
            projectService.Closing -= closing;
            projectService.CloseProjectImmediately();
        }
    }

    [AvaloniaTest]
    public async Task Project_close_is_committed_after_prepare_and_isolates_finalizer_failures()
    {
        var projectService = new ProjectService();
        SetOpenProject("finalizer-failure-commit");
        bool? completionObservedClosed = null;
        bool subsequentFinalizerCalled = false;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (closeContext, _) =>
            {
                closeContext.RegisterCompletion(projectClosed =>
                {
                    completionObservedClosed = projectClosed;
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            };
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> failingFinalizer =
            static (_, cancellationToken) =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                throw new InvalidOperationException("Expected finalizer failure.");
            };
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> subsequentFinalizer =
            (_, cancellationToken) =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                subsequentFinalizerCalled = true;
                return Task.CompletedTask;
            };
        projectService.Closing += closing;
        projectService.ClosingFinalizing += failingFinalizer;
        projectService.ClosingFinalizing += subsequentFinalizer;

        try
        {
            await projectService.CloseProject();

            Assert.Multiple(() =>
            {
                Assert.That(projectService.CurrentProject.Value, Is.Null);
                Assert.That(subsequentFinalizerCalled, Is.True);
                Assert.That(completionObservedClosed, Is.True);
            });
        }
        finally
        {
            projectService.Closing -= closing;
            projectService.ClosingFinalizing -= failingFinalizer;
            projectService.ClosingFinalizing -= subsequentFinalizer;
            projectService.CloseProjectImmediately();
        }
    }

    [AvaloniaTest]
    public async Task MainViewModel_shutdown_coalesces_close_handlers_and_releases_project()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        var closingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClosing = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int closingCalls = 0;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            async (_, cancellationToken) =>
            {
                closingCalls++;
                closingEntered.TrySetResult();
                await releaseClosing.Task.WaitAsync(cancellationToken);
            };

        viewModel.ProjectService.Closing += closing;
        SetOpenProject("async-shutdown");
        try
        {
            Task first = viewModel.ShutdownAsync();
            Task second = viewModel.ShutdownAsync();

            Assert.That(second, Is.SameAs(first));
            await closingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(first.IsCompleted, Is.False);

            releaseClosing.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(closingCalls, Is.EqualTo(1));
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Null);
                Assert.That(BeutlApplication.Current.Project, Is.Null);
            });
        }
        finally
        {
            releaseClosing.TrySetResult();
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task MainViewModel_dispose_only_releases_resources()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        int closingCalls = 0;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing = (_, _) =>
        {
            closingCalls++;
            return Task.CompletedTask;
        };

        viewModel.ProjectService.Closing += closing;
        Project project = SetOpenProject("synchronous-dispose");
        try
        {
            viewModel.Dispose();
            viewModel.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(closingCalls, Is.Zero);
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.SameAs(project));
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(project));
            });
        }
        finally
        {
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            viewModel.Dispose();
        }
    }

    private static Project SetOpenProject(string name)
    {
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}.beutl");
        var project = new Project { Uri = new Uri(Path.GetFullPath(path)) };
        BeutlApplication.Current.Project = project;
        return project;
    }
}
