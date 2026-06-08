using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl;

/// <summary>
/// Helpers that keep in-memory and on-disk project state consistent when a persist step
/// can fail partway through.
/// </summary>
/// <remarks>
/// Internal on purpose: the do/rollback combinator is a generic primitive but its only intended
/// consumers are the project-persistence call sites in this assembly (<see cref="Project"/>) and the
/// <c>Beutl</c> app project. Exposing it publicly would put a project-agnostic utility on the
/// public surface under a project-specific name; if a generic public combinator is ever needed,
/// promote it deliberately under a name that reflects that.
/// </remarks>
internal static class ProjectPersistence
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
}
