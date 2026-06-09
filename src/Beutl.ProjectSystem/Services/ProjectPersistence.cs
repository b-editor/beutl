using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

/// <summary>
/// Orchestrates project persistence: mutating a <see cref="Project"/>'s item list and writing the
/// project to disk as a single step, rolling the in-memory mutation back if the write fails so that
/// in-memory and on-disk state never diverge.
/// </summary>
/// <remarks>
/// This lives in <c>Beutl.ProjectSystem</c> — the project/document-persistence module — rather than
/// on the <see cref="Project"/> domain type, so that <see cref="Project"/> stays a plain model and
/// the "what to do when a write fails" policy lives in one place. The whole type is
/// <c>internal</c>: although <c>Beutl.ProjectSystem</c>'s <c>InternalsVisibleTo</c> makes it
/// reachable from the <c>Beutl</c> app project (its production consumer) and the
/// <c>Beutl.UnitTests</c> assembly (which exercises the rollback behaviour through the
/// <see cref="Action"/>-taking seams), it is not part of any public surface. If a project-agnostic
/// do/rollback combinator (<see cref="PersistOrRollback"/>) is ever needed elsewhere — for example
/// by an out-of-tree plugin — promote it deliberately into a public type under a name that reflects
/// that generality.
/// </remarks>
internal static class ProjectPersistence
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ProjectPersistence));

    /// <summary>
    /// Adds <paramref name="item"/> to <paramref name="project"/> and persists the project to its
    /// own <see cref="CoreObject.Uri"/>. If the write fails, a newly added item is removed again so
    /// the in-memory project stays consistent with what was actually persisted. Items already
    /// present are left untouched (and are not added a second time).
    /// </summary>
    /// <param name="project">The project to mutate and persist.</param>
    /// <param name="item">The item to add.</param>
    internal static void AddItemAndPersist(Project project, ProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(project);
        AddItemAndPersist(project, item, () => StoreProject(project));
    }

    /// <summary>
    /// Removes <paramref name="item"/> from <paramref name="project"/> and persists the project to
    /// its own <see cref="CoreObject.Uri"/>. If the write fails, the item is re-inserted at its
    /// original index so the in-memory project stays consistent with what was actually persisted.
    /// Items not present are left untouched.
    /// </summary>
    /// <param name="project">The project to mutate and persist.</param>
    /// <param name="item">The item to remove.</param>
    internal static void RemoveItemAndPersist(Project project, ProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(project);
        RemoveItemAndPersist(project, item, () => StoreProject(project));
    }

    /// <summary>
    /// Add-and-persist with an injectable persist step. Used as a unit-test seam so the rollback
    /// behaviour can be exercised without real file IO; production callers use the parameterless
    /// <see cref="AddItemAndPersist(Project, ProjectItem)"/> overload.
    /// </summary>
    internal static void AddItemAndPersist(Project project, ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        if (project.Items.Contains(item))
        {
            // Already a member; nothing to roll back if persisting fails.
            persist();
            return;
        }

        project.Items.Add(item);
        PersistOrRollback(persist, () => project.Items.Remove(item));
    }

    /// <summary>
    /// Remove-and-persist with an injectable persist step. Used as a unit-test seam so the rollback
    /// behaviour can be exercised without real file IO; production callers use the parameterless
    /// <see cref="RemoveItemAndPersist(Project, ProjectItem)"/> overload.
    /// </summary>
    internal static void RemoveItemAndPersist(Project project, ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        int index = project.Items.IndexOf(item);
        if (index < 0)
        {
            // Not a member; nothing to roll back if persisting fails.
            persist();
            return;
        }

        project.Items.RemoveAt(index);
        PersistOrRollback(persist, () => project.Items.Insert(index, item));
    }

    /// <summary>
    /// Runs <paramref name="persist"/>; if it throws, runs <paramref name="rollback"/> to undo a
    /// side effect already performed before the call, then rethrows the <b>original</b> persist
    /// exception. If <paramref name="rollback"/> itself throws, that failure is logged and
    /// swallowed so it never masks the original exception.
    /// </summary>
    /// <param name="persist">The persist step that may fail (e.g. writing a file to disk).</param>
    /// <param name="rollback">
    /// Undoes the side effect performed before <paramref name="persist"/> was attempted. A throwing
    /// rollback is tolerated (logged at error level), but callers should still keep it cheap and
    /// best-effort.
    /// </param>
    /// <exception cref="ProjectStateDivergedException">
    /// Thrown when both <paramref name="persist"/> and <paramref name="rollback"/> fail, so that the
    /// caller can surface the now-divergent state to the user. Its
    /// <see cref="Exception.InnerException"/> is the original persist failure.
    /// </exception>
    /// <remarks>
    /// On a persist failure with a successful rollback, the <b>original</b> persist exception is
    /// rethrown (in-memory and on-disk state are consistent again). Only when the rollback itself
    /// fails is the original exception replaced by a <see cref="ProjectStateDivergedException"/> —
    /// because that case is categorically worse (unrecoverable divergence) and must not masquerade
    /// as an ordinary save failure.
    /// <para>
    /// This is intentionally synchronous: all current persistence (<c>CoreSerializer.StoreToUri</c>)
    /// is synchronous. If an async persist step is ever needed, add a <c>Task</c>-returning overload
    /// rather than blocking on a <see cref="Task"/> inside an <see cref="Action"/>.
    /// </para>
    /// </remarks>
    internal static void PersistOrRollback(Action persist, Action rollback)
    {
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(rollback);

        try
        {
            persist();
        }
        catch (Exception persistEx)
        {
            try
            {
                rollback();
            }
            catch (Exception rollbackEx)
            {
                // Both steps failed: in-memory and on-disk state are now divergent and
                // unrecoverable — exactly the condition this helper exists to prevent. Log at error
                // level and raise a distinct exception so the caller can warn the user instead of
                // reporting a plain save failure. The original persist failure is preserved as the
                // inner exception.
                s_logger.LogError(rollbackEx, "Rollback after a failed persist also failed.");
                throw new ProjectStateDivergedException(persistEx, rollbackEx);
            }

            // Rollback succeeded; surface the original persist failure unchanged.
            throw;
        }
    }

    private static void StoreProject(Project project)
    {
        Uri uri = project.Uri
            ?? throw new InvalidOperationException("The project has no Uri to persist to.");
        CoreSerializer.StoreToUri(project, uri);
    }
}
