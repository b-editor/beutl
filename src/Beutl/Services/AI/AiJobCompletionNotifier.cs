using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.AI;

internal sealed class AiJobCompletionNotifier : IDisposable
{
    private readonly Action _openJobCenter;
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly AiJobResultHandlerRegistry _resultHandlers;
    private readonly IDisposable _subscription;
    private readonly ILogger _logger = Log.CreateLogger<AiJobCompletionNotifier>();
    private Dictionary<AiJobId, AiJobStatusSemantics> _knownStatuses = [];
    private bool _hasBaseline;

    public AiJobCompletionNotifier(
        IObservable<AiJobMonitorSnapshot> snapshots,
        IAiJobKindRegistry jobKinds,
        AiJobResultHandlerRegistry resultHandlers,
        Action openJobCenter)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(openJobCenter);

        _jobKinds = jobKinds ?? throw new ArgumentNullException(nameof(jobKinds));
        _resultHandlers = resultHandlers ?? throw new ArgumentNullException(nameof(resultHandlers));
        _openJobCenter = openJobCenter;
        _subscription = snapshots.Subscribe(ProcessSnapshot);
    }

    public void Dispose() => _subscription.Dispose();

    internal void ProcessSnapshot(AiJobMonitorSnapshot snapshot)
    {
        if (snapshot.Error is AuthenticationRequiredException
            || snapshot.IsLoading && snapshot.Jobs.IsEmpty)
        {
            _knownStatuses.Clear();
            _hasBaseline = false;
            return;
        }

        if (snapshot.IsLoading || snapshot.Error is not null)
            return;

        var currentStatuses = new Dictionary<AiJobId, AiJobStatusSemantics>();
        foreach (AiJob job in snapshot.Jobs)
        {
            try
            {
                currentStatuses[job.Id] = _jobKinds.GetStatus(job);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve AI job status for {JobId}.", job.Id);
                currentStatuses[job.Id] = AiJobStatusSemantics.Unknown;
            }
        }

        if (!_hasBaseline)
        {
            _knownStatuses = currentStatuses;
            _hasBaseline = true;
            return;
        }

        foreach (AiJob job in snapshot.Jobs)
        {
            if (!_knownStatuses.TryGetValue(job.Id, out AiJobStatusSemantics oldStatus)
                || oldStatus.IsTerminal
                || !currentStatuses[job.Id].IsTerminal)
            {
                continue;
            }

            ShowCompletion(job, currentStatuses[job.Id]);
        }

        _knownStatuses = currentStatuses;
    }

    private void ShowCompletion(AiJob job, AiJobStatusSemantics status)
    {
        if (!_resultHandlers.TryAcquire(job.Kind, out IAiJobResultHandlerLease? lease))
            return;

        using (lease)
        {
            try
            {
                IAiJobResultHandler handler = lease.Handler;
                AiJobPresentation presentation = handler.Present(job, status);
                AiJobCompletionPresentation? completion = handler.CreateCompletion(
                    job,
                    status,
                    presentation);
                if (completion is null)
                    return;

                NotificationService.Show(
                    completion.Title,
                    completion.Message,
                    ToNotificationType(completion.Notification),
                    completion.Expiration,
                    actions: [new NotificationAction(Strings.AiJobCenter, _openJobCenter)]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI job completion handler failed for {JobId}.", job.Id);
            }
        }
    }

    private static NotificationType ToNotificationType(AiJobNotificationKind notification)
        => notification switch
        {
            AiJobNotificationKind.Information => NotificationType.Information,
            AiJobNotificationKind.Success => NotificationType.Success,
            AiJobNotificationKind.Warning => NotificationType.Warning,
            _ => throw new ArgumentOutOfRangeException(nameof(notification)),
        };
}
