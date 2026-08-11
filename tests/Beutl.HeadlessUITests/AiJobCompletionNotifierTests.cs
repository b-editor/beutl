using System.Reactive.Subjects;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiJobCompletionNotifierTests
{
    [Test]
    public void ActiveToTerminalTransition_NotifiesOnceAndProvidesJobCenterAction()
    {
        INotificationServiceHandler? previousHandler = NotificationService.Handler;
        var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            using var snapshots = new Subject<AiJobMonitorSnapshot>();
            using AiJobKindRegistry jobKinds = CreateBuiltInRegistry();
            using var resultHandlers = CreateBuiltInResultHandlers();
            int openCount = 0;
            using var notifier = new AiJobCompletionNotifier(
                snapshots,
                jobKinds,
                resultHandlers,
                () => openCount++);
            AiJob queued = CreateJob(AiJobStatuses.Queued);

            snapshots.OnNext(new AiJobMonitorSnapshot([queued], null, false, null));
            snapshots.OnNext(new AiJobMonitorSnapshot([queued with { Status = AiJobStatuses.Succeeded }], null, true, null));
            Assert.That(handler.Notifications, Is.Empty);

            snapshots.OnNext(new AiJobMonitorSnapshot([queued with { Status = AiJobStatuses.Succeeded }], null, false, null));
            snapshots.OnNext(new AiJobMonitorSnapshot([queued with { Status = AiJobStatuses.Succeeded }], null, false, null));

            Assert.That(handler.Notifications, Has.Count.EqualTo(1));
            Notification notification = handler.Notifications[0];
            Assert.Multiple(() =>
            {
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Success));
                Assert.That(notification.Actions, Has.Count.EqualTo(1));
            });

            notification.Actions![0].Callback();
            Assert.That(openCount, Is.EqualTo(1));
        }
        finally
        {
            NotificationService.Handler = previousHandler ?? NullNotificationHandler.Instance;
        }
    }

    [Test]
    public void FailedTransition_ShowsUsageRestoredWarning()
    {
        INotificationServiceHandler? previousHandler = NotificationService.Handler;
        var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            using var snapshots = new Subject<AiJobMonitorSnapshot>();
            using AiJobKindRegistry jobKinds = CreateBuiltInRegistry();
            using var resultHandlers = CreateBuiltInResultHandlers();
            using var notifier = new AiJobCompletionNotifier(
                snapshots,
                jobKinds,
                resultHandlers,
                () => { });
            AiJob running = CreateJob(AiJobStatuses.Running);

            snapshots.OnNext(new AiJobMonitorSnapshot([running], null, false, null));
            snapshots.OnNext(new AiJobMonitorSnapshot([running with { Status = AiJobStatuses.Failed }], null, false, null));

            Assert.That(handler.Notifications, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(handler.Notifications[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(
                    handler.Notifications[0].Message,
                    Is.EqualTo(string.Format(
                        Strings.AiJobCenter_FailedNotification,
                        Strings.AiVideoGeneration)));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler ?? NullNotificationHandler.Instance;
        }
    }

    [Test]
    public void CanceledTransition_ShowsNeutralCanceledNotification()
    {
        INotificationServiceHandler? previousHandler = NotificationService.Handler;
        var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            using var snapshots = new Subject<AiJobMonitorSnapshot>();
            using AiJobKindRegistry jobKinds = CreateBuiltInRegistry();
            using var resultHandlers = CreateBuiltInResultHandlers();
            using var notifier = new AiJobCompletionNotifier(
                snapshots,
                jobKinds,
                resultHandlers,
                () => { });
            AiJob running = CreateJob(AiJobStatuses.Running);

            snapshots.OnNext(new AiJobMonitorSnapshot([running], null, false, null));
            snapshots.OnNext(new AiJobMonitorSnapshot(
                [running with { Status = AiJobStatuses.Canceled }],
                null,
                false,
                null));

            Assert.That(handler.Notifications, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(handler.Notifications[0].Type, Is.EqualTo(NotificationType.Information));
                Assert.That(
                    handler.Notifications[0].Message,
                    Is.EqualTo(string.Format(
                        Strings.AiJobCenter_CanceledNotification,
                        Strings.AiVideoGeneration)));
                Assert.That(
                    handler.Notifications[0].Message,
                    Is.Not.EqualTo(string.Format(
                        Strings.AiJobCenter_FailedNotification,
                        Strings.AiVideoGeneration)));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler ?? NullNotificationHandler.Instance;
        }
    }

    [Test]
    public void CustomKind_ControlsTerminalOutcomePresentationAndCompletion()
    {
        INotificationServiceHandler? previousHandler = NotificationService.Handler;
        var notificationHandler = new CaptureNotificationHandler();
        NotificationService.Handler = notificationHandler;
        try
        {
            using var jobKinds = new AiJobKindRegistry();
            var descriptor = new AiJobKindDescriptor(
                new AiJobKindId("vendor.upscale"),
                new AiJobStatusMap(
                [
                    KeyValuePair.Create(
                        new AiJobStatusId("waiting-for-gpu"),
                        new AiJobStatusSemantics(false, false)),
                    KeyValuePair.Create(
                        new AiJobStatusId("ready-for-review"),
                        new AiJobStatusSemantics(
                            true,
                            false,
                            new AiJobOutcomeId("vendor.review"))),
                ]));
            using IAiJobKindRegistration registration = jobKinds.Register(descriptor);
            using var resultHandlers = new AiJobResultHandlerRegistry(
            [
                new AiJobResultHandlerRegistration(new AiJobResultContribution(
                    new AiJobKindId("vendor.upscale"),
                    new CustomResultHandler())),
            ]);
            using var snapshots = new Subject<AiJobMonitorSnapshot>();
            using var notifier = new AiJobCompletionNotifier(
                snapshots,
                jobKinds,
                resultHandlers,
                () => { });
            AiJob waiting = CreateJob(
                new AiJobStatusId("waiting-for-gpu"),
                new AiJobKindId("vendor.upscale"));

            snapshots.OnNext(new AiJobMonitorSnapshot([waiting], null, false, null));
            snapshots.OnNext(new AiJobMonitorSnapshot(
                [waiting with { Status = new AiJobStatusId("ready-for-review") }],
                null,
                false,
                null));

            Assert.That(notificationHandler.Notifications, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(notificationHandler.Notifications[0].Title, Is.EqualTo("Vendor review"));
                Assert.That(notificationHandler.Notifications[0].Message, Is.EqualTo("Upscale is ready"));
                Assert.That(notificationHandler.Notifications[0].Type, Is.EqualTo(NotificationType.Information));
            }
        }
        finally
        {
            NotificationService.Handler = previousHandler ?? NullNotificationHandler.Instance;
        }
    }

    private static AiJobKindRegistry CreateBuiltInRegistry()
        => AiJobKindRegistry.CreateBuiltIn(
            new UnusedImageGenerationService(),
            new UnusedVideoService(),
            new UnusedEntitlementService());

    private static AiJobResultHandlerRegistry CreateBuiltInResultHandlers()
        => new(BuiltInAiJobResultHandlers.Create());

    private static AiJob CreateJob(AiJobStatusId status, AiJobKindId? kind = null)
        => new(
            new AiJobId("job-1"),
            kind ?? AiJobKinds.Video,
            status,
            null,
            null,
            null,
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);
    }

    private sealed class NullNotificationHandler : INotificationServiceHandler
    {
        public static NullNotificationHandler Instance { get; } = new();

        public void Show(Notification notification)
        {
        }
    }

    private sealed class CustomResultHandler : IAiJobResultHandler
    {
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => new("Vendor upscale", "Ready for review", "Upscale", string.Empty, false);

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status,
            AiJobPresentation presentation)
            => status.Outcome == new AiJobOutcomeId("vendor.review")
                ? new AiJobCompletionPresentation(
                    "Vendor review",
                    "Upscale is ready",
                    AiJobNotificationKind.Information)
                : null;

        public bool CanHandle(AiJob job, AiJobStatusSemantics status) => false;

        public Task HandleAsync(
            AiJob job,
            IAiJobResultContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedImageGenerationService : IAiImageGenerationService
    {
        public Task<AiImageResult> GenerateAsync(
            AiImageGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedVideoService : IAiVideoService
    {
        public Task<AiVideoGenerationResult> CreateAsync(
            AiVideoGenerationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AiVideoJob> GetAsync(AiJobId jobId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedEntitlementService : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; } =
            new ReactivePropertySlim<AiEntitlements?>();

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
