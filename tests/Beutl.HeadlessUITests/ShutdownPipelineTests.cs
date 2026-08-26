using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Rendering;
using Beutl.Extensibility;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Reactive.Bindings;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

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
    public async Task Coordinator_keeps_the_window_open_when_the_close_was_abandoned()
    {
        int cleanupCalls = 0;
        int closeCalls = 0;
        var coordinator = new WindowShutdownCoordinator(
            _ =>
            {
                cleanupCalls++;
                return cleanupCalls == 1
                    ? Task.FromException(
                        new ProjectCloseAbortedException("the project could not be saved"))
                    : Task.CompletedTask;
            },
            () => closeCalls++);

        await coordinator.BeginShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            // Closing here would discard the very edits the abandoned close kept in their editors.
            Assert.That(closeCalls, Is.Zero);
            Assert.That(coordinator.CanClose, Is.False);
        });

        // The next attempt has to run the shutdown again instead of joining the abandoned one.
        await coordinator.BeginShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(cleanupCalls, Is.EqualTo(2));
            Assert.That(closeCalls, Is.EqualTo(1));
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
            await viewModel.DisposeAsync().AsTask();

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
            await viewModel.DisposeAsync().AsTask();
        }
    }

    [AvaloniaTest]
    public async Task MainViewModel_aborted_shutdown_keeps_agent_host_usable_until_terminal_close()
    {
        await TestReset.ResetShellAsync();
        int stopCalls = 0;
        var viewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "shutdown-test-token",
                _ =>
                {
                    stopCalls++;
                    return Task.CompletedTask;
                }));
        SetOpenProject("shutdown-agent-restart");
        var editorContext = new ShutdownEditorContext();
        viewModel.EditorService.TabItems.Add(new EditorTabItem(editorContext));
        int closeAttempts = 0;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (_, _) =>
            {
                Assert.That(editorContext.IsEnabled.Value, Is.False);
                return ++closeAttempts == 1
                    ? Task.FromException(new ProjectCloseAbortedException("save failed"))
                    : Task.CompletedTask;
            };
        viewModel.ProjectService.ClosingPreparing += closing;
        try
        {
            await viewModel.AgentHostEndpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.True);
            Task aborted = viewModel.ShutdownAsync();
            ProjectCloseAbortedException? abortedException = null;
            try
            {
                await aborted.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ProjectCloseAbortedException ex)
            {
                abortedException = ex;
            }
            Assert.That(abortedException, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.True);
                Assert.That(stopCalls, Is.Zero);
                Assert.That(editorContext.IsEnabled.Value, Is.True);
            });
            await viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(stopCalls, Is.EqualTo(1));
                Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.False);
            });
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Shutdown_drains_detached_render_lease_before_project_closing()
    {
        await TestReset.ResetShellAsync();
        var manager = new RenderJobManager();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new TestLease();
        EditorService? capturedEditorService = null;
        var viewModel = new MainViewModel((projectService, editorService) =>
        {
            capturedEditorService = editorService;
            return new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "shutdown-render-drain-token",
                _ => Task.CompletedTask,
                null,
                () => manager);
        });
        SetOpenProject("shutdown-render-drain");
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (_, _) =>
            {
                Assert.That(started.Task.IsCompleted, Is.True);
                Assert.That(lease.DisposeCount, Is.EqualTo(1));
                using IDisposable? mutation = viewModel.EditorService.TryBeginWorktreeMutation();
                Assert.That(mutation, Is.Not.Null);
                return Task.CompletedTask;
            };
        viewModel.ProjectService.ClosingPreparing += closing;
        try
        {
            await viewModel.AgentHostEndpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            IDisposable outputLease = capturedEditorService!.TryBeginOutputOperation()!;
            lease = new TestLease(outputLease);
            manager.Enqueue("detached", async token =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                    await releaseCleanup.Task;
                    throw;
                }

                return new JsonObject();
            }, lease);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task shutdown = viewModel.ShutdownAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(shutdown.IsCompleted, Is.False);
            releaseCleanup.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(lease.DisposeCount, Is.EqualTo(1));
        }
        finally
        {
            viewModel.ProjectService.ClosingPreparing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Main_shutdown_cancellation_is_retryable_after_quiescence_cleanup()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "shutdown-cancel-retry-token"));
        SetOpenProject("shutdown-cancel-retry");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await viewModel.AgentHostEndpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await viewModel.ShutdownAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Canceled shutdown should not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }
            Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.True);
            await viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.False);
        }
        finally
        {
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Main_shutdown_cancellation_during_close_is_retryable_and_keeps_composition_usable()
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "shutdown-close-cancel-token"));
        Project project = SetOpenProject("shutdown-close-cancel");
        var closingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            async (_, cancellationToken) =>
            {
                closingEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            };
        viewModel.ProjectService.Closing += closing;
        using var cancellation = new CancellationTokenSource();

        try
        {
            await viewModel.AgentHostEndpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", viewModel.AgentHostEndpoint.Token);

            Task shutdown = viewModel.ShutdownAsync(cancellation.Token);
            await closingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            OperationCanceledException? canceledException = null;
            try
            {
                await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Cancellation during project close should be retryable.");
            }
            catch (OperationCanceledException ex)
            {
                canceledException = ex;
            }

            using (IDisposable? editorLease = viewModel.EditorService.TryBeginWorktreeMutation())
            {
                Assert.That(editorLease, Is.Not.Null);
            }
            using HttpResponseMessage response =
                await client.GetAsync(viewModel.AgentHostEndpoint.EndpointUri);

            Assert.Multiple(() =>
            {
                Assert.That(canceledException, Is.Not.Null);
                Assert.That(canceledException!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.SameAs(project));
                Assert.That(BeutlApplication.Current.Project, Is.SameAs(project));
                Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.True);
                Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));
                Assert.That(viewModel.IsDisposed, Is.False);
            });

            viewModel.ProjectService.Closing -= closing;
            await viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.False);
        }
        finally
        {
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task MainViewModel_late_closing_abort_keeps_agent_host_usable_until_terminal_close()
    {
        await TestReset.ResetShellAsync();
        int stopCalls = 0;
        var viewModel = new MainViewModel((projectService, editorService) =>
            new AgentHostEndpoint(
                projectService,
                editorService,
                GetAvailableLoopbackPort(),
                "shutdown-late-abort-token",
                _ =>
                {
                    stopCalls++;
                    return Task.CompletedTask;
                }));
        SetOpenProject("shutdown-late-abort");
        int closeAttempts = 0;
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (_, _) => ++closeAttempts == 1
                ? Task.FromException(new ProjectCloseAbortedException("save failed"))
                : Task.CompletedTask;
        viewModel.ProjectService.Closing += closing;
        try
        {
            await viewModel.AgentHostEndpoint.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Task aborted = viewModel.ShutdownAsync();
            ProjectCloseAbortedException? abortedException = null;
            try
            {
                await aborted.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ProjectCloseAbortedException ex)
            {
                abortedException = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(abortedException, Is.Not.Null);
                Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.True);
                Assert.That(stopCalls, Is.Zero);
            });

            await viewModel.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(stopCalls, Is.EqualTo(1));
                Assert.That(viewModel.AgentHostEndpoint.IsRunning, Is.False);
            });
        }
        finally
        {
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task MainViewModel_dispose_closes_the_project_before_releasing_resources()
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
        SetOpenProject("synchronous-dispose");
        try
        {
            viewModel.Dispose();
            viewModel.Dispose();
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(closingCalls, Is.EqualTo(1));
                Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Null);
                Assert.That(BeutlApplication.Current.Project, Is.Null);
            });
        }
        finally
        {
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Menu_close_command_handles_close_failures_without_faulting(bool unexpected)
    {
        await TestReset.ResetShellAsync();
        var viewModel = new MainViewModel();
        SetOpenProject("menu-close-aborted");
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<ProjectService.ProjectCloseContext, CancellationToken, Task> closing =
            (_, _) =>
            {
                invoked.TrySetResult();
                return Task.FromException(unexpected
                    ? new InvalidOperationException("close failed")
                    : new ProjectCloseAbortedException("save failed"));
            };
        viewModel.ProjectService.Closing += closing;
        try
        {
            viewModel.MenuBar.CloseProject.Execute();
            await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await viewModel.MenuBar.CloseProjectCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!viewModel.MenuBar.CloseProject.CanExecute())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("CloseProject did not re-enable after completion.");
                }
                await Task.Delay(10);
            }
            Assert.That(viewModel.ProjectService.CurrentProject.Value, Is.Not.Null);
            Assert.That(viewModel.MenuBar.CloseProject.CanExecute(), Is.True);
        }
        finally
        {
            viewModel.ProjectService.Closing -= closing;
            viewModel.ProjectService.CloseProjectImmediately();
            await viewModel.DisposeAsync();
        }
    }

    private static Project SetOpenProject(string name)
    {
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}.beutl");
        var project = new Project { Uri = new Uri(Path.GetFullPath(path)) };
        BeutlApplication.Current.Project = project;
        return project;
    }

    private static int GetAvailableLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback,
            0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ShutdownEditorContext : IEditorContext
    {
        public CoreObject Object { get; } = new Scene
        {
            Uri = new Uri("file:///shutdown-editor.scene"),
        };

        public EditorExtension Extension => SceneEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestLease : IDisposable
    {
        private int _disposeCount;
        private readonly IDisposable? _inner;
        public TestLease(IDisposable? inner = null) => _inner = inner;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _inner?.Dispose();
            }
        }
    }
}
