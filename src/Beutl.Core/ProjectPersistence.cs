using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl;

/// <summary>
/// Helpers that keep in-memory and on-disk project state consistent when a persist step
/// can fail partway through.
/// </summary>
public static class ProjectPersistence
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ProjectPersistence));

    /// <summary>
    /// Runs <paramref name="persist"/>; if it throws, runs <paramref name="rollback"/> to undo a
    /// side effect already performed before the call, then rethrows the <b>original</b> persist
    /// exception. If <paramref name="rollback"/> itself throws, that failure is logged and
    /// swallowed so it never masks the original exception.
    /// </summary>
    /// <param name="persist">The persist step that may fail (e.g. writing a file to disk).</param>
    /// <param name="rollback">
    /// Undoes the side effect performed before <paramref name="persist"/> was attempted. A throwing
    /// rollback is tolerated (logged, not propagated), but callers should still keep it cheap and
    /// best-effort.
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
                // Surface the original persist failure, not the rollback error; keep the rollback
                // failure in the log so it is not lost entirely.
                s_logger.LogWarning(rollbackEx, "Rollback after a failed persist also failed.");
            }

            throw;
        }
    }
}
