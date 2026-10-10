using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TitleBarStatusViewModelTests
{
    [AvaloniaTest]
    public async Task Busy_follows_startup_tasks_running_outputs_and_running_ai_jobs()
    {
        var startup = new ReactivePropertySlim<bool>();
        var snapshots = new ReactivePropertySlim<AiJobMonitorSnapshot>(AiJobMonitorSnapshot.Empty);
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        await using var endpoint = new AgentHostEndpoint(
            new ProjectService(), editorService, AgentHostEndpoint.DefaultPort, "test-token");
        using var status = new TitleBarStatusViewModel(
            startup,
            editorService,
            endpoint,
            Observable.Return<AuthenticatedUser?>(null),
            snapshots,
            CreateJobKinds(),
            () => { });
        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(status.IsBusy.Value, Is.False);
            Assert.That(status.ToolTip.Value, Is.EqualTo(StatusStrings.Title));
            Assert.That(status.AccountText.Value, Is.EqualTo(StatusStrings.NotSignedIn));
        });

        startup.Value = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(status.IsBusy.Value, Is.True);
            Assert.That(status.StartupText.Value, Is.EqualTo(StatusStrings.Running));
            Assert.That(status.ToolTip.Value, Does.Contain(Strings.RunningStartupTasks));
        });

        startup.Value = false;
        using (OutputProfileItem output = CreateOutput(editorService))
        using (editorService.TrackRunningOutput(output))
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(status.IsBusy.Value, Is.True);
                Assert.That(status.StartupText.Value, Is.EqualTo(StatusStrings.Done));
                Assert.That(status.RunningOutputs.Value, Is.EqualTo(new[] { "intro.scene - Default" }));
            });
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(status.IsBusy.Value, Is.False);
            Assert.That(status.HasRunningOutputs.Value, Is.False);
        });

        snapshots.Value = new AiJobMonitorSnapshot(
            [CreateJob("running-job", "running"), CreateJob("finished-job", "succeeded")], null, false, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Multiple(() =>
        {
            Assert.That(status.RunningAiJobCount.Value, Is.EqualTo(1));
            Assert.That(status.IsBusy.Value, Is.True);
            Assert.That(status.AiJobsText.Value, Is.EqualTo(string.Format(StatusStrings.RunningCount, 1)));
        });
    }

    [AvaloniaTest]
    public async Task Opening_the_popup_samples_resources_and_services()
    {
        var editorService = new EditorService(new ExtensionProvider(), (_, _) => { });
        await using var endpoint = new AgentHostEndpoint(
            new ProjectService(), editorService, AgentHostEndpoint.DefaultPort, "test-token");
        using var status = new TitleBarStatusViewModel(
            Observable.Return(false),
            editorService,
            endpoint,
            Observable.Return<AuthenticatedUser?>(null),
            Observable.Return(AiJobMonitorSnapshot.Empty),
            CreateJobKinds(),
            () => { });

        status.OnPopupOpened();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(status.ProcessMemoryText.Value, Is.Not.EqualTo(StatusStrings.Loading));
                Assert.That(status.ManagedHeapText.Value, Is.Not.EqualTo(StatusStrings.Loading));
                Assert.That(status.LiveMcpText.Value, Is.EqualTo(StatusStrings.Stopped));
                Assert.That(status.LiveMcpEndpoint.Value, Is.Null);
            });

            // The frame cache is counted off the UI thread.
            for (int i = 0; i < 50 && status.FrameCacheText.Value == StatusStrings.Loading; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.That(status.FrameCacheText.Value, Is.Not.EqualTo(StatusStrings.Loading));
        }
        finally
        {
            status.OnPopupClosed();
        }
    }

    private static IAiJobKindRegistry CreateJobKinds()
    {
        var jobKinds = new Mock<IAiJobKindRegistry>();
        jobKinds.Setup(registry => registry.GetStatus(It.IsAny<AiJob>()))
            .Returns((AiJob job) => job.Status.Value == "running"
                ? new AiJobStatusSemantics(isTerminal: false, shouldPoll: true)
                : new AiJobStatusSemantics(isTerminal: true, shouldPoll: false));
        return jobKinds.Object;
    }

    private static OutputProfileItem CreateOutput(EditorService editorService)
    {
        var context = new Mock<IOutputContext>();
        context.SetupGet(c => c.Name).Returns(new ReactivePropertySlim<string>("Default"));
        context.SetupGet(c => c.Object).Returns(new Project { Uri = new Uri("file:///work/intro.scene") });
        return new OutputProfileItem(context.Object, Mock.Of<IEditorContext>(), editorService);
    }

    private static AiJob CreateJob(string id, string status)
    {
        var time = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        return new AiJob(
            new AiJobId(id),
            new AiJobKindId("image"),
            new AiJobStatusId(status),
            null,
            null,
            null,
            null,
            false,
            time,
            time);
    }
}
