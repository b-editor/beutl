using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

/// <summary>
/// Mutates a <see cref="Project"/>'s item list and writes the project to disk as one step, rolling
/// the in-memory mutation back if the write fails so the two never diverge.
/// </summary>
/// <remarks>
/// Lives in <c>Beutl.ProjectSystem</c> (not on <see cref="Project"/>) so the model stays plain and
/// the failure policy lives in one place. <c>internal</c>, reachable via <c>InternalsVisibleTo</c>
/// from the app and unit tests.
/// </remarks>
internal static class ProjectPersistence
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ProjectPersistence));

    /// <summary>
    /// Adds <paramref name="item"/> and persists the project. If the write fails the add is rolled
    /// back; an item already present is left untouched (and not added twice).
    /// </summary>
    internal static void AddItemAndPersist(Project project, ProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(project);
        AddItemAndPersist(project, item, () => StoreProject(project));
    }

    /// <summary>
    /// Removes <paramref name="item"/> and persists the project. If the write fails the item is
    /// re-inserted at its original index; an item not present is left untouched.
    /// </summary>
    internal static void RemoveItemAndPersist(Project project, ProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(project);
        RemoveItemAndPersist(project, item, () => StoreProject(project));
    }

    /// <summary>A unit-test seam that replaces the persist step.</summary>
    internal static void AddItemAndPersist(Project project, ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        if (project.Items.Contains(item))
        {
            // Already a member; nothing to roll back.
            persist();
            return;
        }

        project.Items.Add(item);
        PersistOrRollback(persist, () => project.Items.Remove(item));
    }

    /// <summary>A unit-test seam that replaces the persist step.</summary>
    internal static void RemoveItemAndPersist(Project project, ProjectItem item, Action persist)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persist);

        int index = project.Items.IndexOf(item);
        if (index < 0)
        {
            // Not a member; nothing to roll back.
            persist();
            return;
        }

        project.Items.RemoveAt(index);
        PersistOrRollback(persist, () => project.Items.Insert(index, item));
    }

    /// <summary>
    /// Runs <paramref name="persist"/>; if it throws, runs <paramref name="rollback"/> and rethrows
    /// the <b>original</b> persist exception. If <paramref name="rollback"/> also throws, the state
    /// is unrecoverably divergent, so a <see cref="ProjectStateDivergedException"/> (carrying the
    /// original failure as its inner exception) is thrown instead.
    /// </summary>
    /// <param name="persist">The persist step that may fail (e.g. writing a file to disk).</param>
    /// <param name="rollback">Best-effort undo of the side effect performed before persisting.</param>
    /// <exception cref="ProjectStateDivergedException">Both steps failed.</exception>
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
                // Both failed: state is now unrecoverably divergent. Raise a distinct exception
                // (original failure kept as inner) so the caller can warn the user.
                s_logger.LogError(rollbackEx, "Rollback after a failed persist also failed.");
                throw new ProjectStateDivergedException(persistEx, rollbackEx);
            }

            // Rollback succeeded; rethrow the original failure.
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
