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
/// the "what to do when a write fails" policy lives in one place. The combinator
/// <see cref="PersistOrRollback"/> is intentionally not exposed publicly: although
/// <c>Beutl.ProjectSystem</c>'s <c>InternalsVisibleTo</c> makes it reachable from a few sibling
/// assemblies, the only intended consumers are the project-persistence call sites in the
/// <c>Beutl</c> app project. If a project-agnostic do/rollback combinator is ever needed elsewhere,
/// promote it deliberately under a name that reflects that generality.
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
    public static void AddItemAndPersist(Project project, ProjectItem item)
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
    public static void RemoveItemAndPersist(Project project, ProjectItem item)
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
    /// rollback is tolerated (logged at error level, not propagated), but callers should still keep
    /// it cheap and best-effort. Note that a throwing rollback leaves in-memory and on-disk state
    /// divergent in a way the caller cannot detect from the rethrown exception.
    /// </param>
    /// <remarks>
    /// This is intentionally synchronous: all current persistence (<c>CoreSerializer.StoreToUri</c>)
    /// is synchronous. If an async persist step is ever needed, add a <c>Task</c>-returning overload
    /// rather than blocking on a <see cref="Task"/> inside an <see cref="Action"/>.
    /// </remarks>
    public static void PersistOrRollback(Action persist, Action rollback)
    {
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(rollback);

        try
        {
            persist();
        }
        catch
        {
            try
            {
                rollback();
            }
            catch (Exception rollbackEx)
            {
                // Surface the original persist failure, not the rollback error. Log at error level:
                // a failed rollback leaves in-memory and on-disk state divergent and unrecoverable,
                // which is exactly the condition this helper exists to prevent.
                s_logger.LogError(rollbackEx, "Rollback after a failed persist also failed.");
            }

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
