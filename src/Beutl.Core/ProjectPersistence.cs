namespace Beutl;

/// <summary>
/// Helpers that keep in-memory and on-disk project state consistent when a persist step
/// can fail partway through.
/// </summary>
public static class ProjectPersistence
{
    /// <summary>
    /// Runs <paramref name="persist"/>; if it throws, runs <paramref name="rollback"/> to undo a
    /// side effect already performed before the call, then rethrows the original exception.
    /// </summary>
    /// <param name="persist">The persist step that may fail (e.g. writing a file to disk).</param>
    /// <param name="rollback">
    /// Undoes the side effect performed before <paramref name="persist"/> was attempted. It must
    /// not throw; callers that perform best-effort cleanup are expected to swallow/log internally.
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
            rollback();
            throw;
        }
    }
}
