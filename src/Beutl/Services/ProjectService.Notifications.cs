using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
    private async Task<IReadOnlyList<ProjectOpenPreparation>> NotifyOpeningPreflightAsync(
        ProjectOpenAttempt attempt)
    {
        if (OpeningPreflight is not { } openingPreflight)
        {
            return [];
        }

        var preparations = new List<ProjectOpenPreparation>();
        foreach (Func<ProjectOpenAttempt, CancellationToken, Task<ProjectOpenPreparation?>> handler
                 in openingPreflight.GetInvocationList())
        {
            attempt.CancellationToken.ThrowIfCancellationRequested();
            ProjectOpenPreparation? preparation = await handler(
                attempt,
                attempt.CancellationToken);
            if (preparation is not null)
            {
                preparations.Add(preparation);
            }
        }

        return preparations;
    }

    private async Task NotifyOpenedAsync(Project project)
    {
        if (Opened is { } opened)
        {
            foreach (Func<Project, Task> handler in opened.GetInvocationList())
            {
                await handler(project);
            }
        }
    }

    private IEnumerable<Func<ProjectCloseContext, CancellationToken, Task>>
        EnumerateRollbackCloseHandlers()
    {
        if (ClosingPreparing is { } closingPreparing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closingPreparing.GetInvocationList())
            {
                yield return handler;
            }
        }

        if (Closing is { } closing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closing.GetInvocationList())
            {
                yield return handler;
            }
        }
    }

    private async Task NotifyClosingPreparingAsync(
        ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (ClosingPreparing is { } closingPreparing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closingPreparing.GetInvocationList())
            {
                await handler(closeContext, cancellationToken);
            }
        }
    }

    private async Task NotifyClosingAsync(
        ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (Closing is { } closing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closing.GetInvocationList())
            {
                await handler(closeContext, cancellationToken);
            }
        }
    }

    private async Task NotifyClosingFinalizingAsync(ProjectCloseContext closeContext)
    {
        if (ClosingFinalizing is { } closingFinalizing)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closingFinalizing.GetInvocationList())
            {
                try
                {
                    await handler(closeContext, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A project-close finalizer failed.");
                }
            }
        }
    }

    private void PublishProjectChange((Project? New, Project? Old) change)
    {
        try
        {
            _projectObservable.OnNext(change);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to publish a committed project-state transition.");
        }
    }

    private void PublishTransitionCommitted(Project? project)
    {
        if (TransitionCommitted is not { } transitionCommitted)
        {
            return;
        }

        foreach (Action<Project?> handler in transitionCommitted.GetInvocationList())
        {
            try
            {
                handler(project);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A committed project-transition handler failed.");
            }
        }
    }
}
