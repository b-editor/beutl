using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class ProjectService
{
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

    private static async Task InvokeCloseHandlersAsync(
        Func<ProjectCloseContext, CancellationToken, Task>? handlers,
        ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (handlers is { } closeHandlers)
        {
            foreach (Func<ProjectCloseContext, CancellationToken, Task> handler
                     in closeHandlers.GetInvocationList())
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

    private void PublishTransitionCommitted(ProjectTransitionCommit commit)
    {
        if (TransitionCommitted is not { } transitionCommitted)
        {
            return;
        }

        foreach (Action<ProjectTransitionCommit> handler in transitionCommitted.GetInvocationList())
        {
            try
            {
                handler(commit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A committed project-transition handler failed.");
            }
        }
    }
}
