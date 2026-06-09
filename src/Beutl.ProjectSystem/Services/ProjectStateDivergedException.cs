namespace Beutl.Services;

/// <summary>
/// Thrown when persisting a project failed and the subsequent rollback also failed, leaving the
/// in-memory project and its on-disk file divergent — the unrecoverable condition that
/// <see cref="ProjectPersistence.PersistOrRollback"/> otherwise prevents. Callers should surface
/// this to the user (e.g. "reopen the project") rather than treat it as an ordinary save failure.
/// </summary>
/// <remarks>
/// <see cref="Exception.InnerException"/> is the original persist failure (the actionable cause,
/// e.g. "disk full"); <see cref="RollbackException"/> is the rollback failure that left the state
/// divergent.
/// </remarks>
public sealed class ProjectStateDivergedException : Exception
{
    public ProjectStateDivergedException(Exception persistException, Exception rollbackException)
        : base(
            "The project could not be saved and the in-memory change could not be rolled back; "
                + "in-memory and on-disk state have diverged.",
            persistException)
    {
        ArgumentNullException.ThrowIfNull(rollbackException);
        RollbackException = rollbackException;
    }

    /// <summary>The rollback failure that left in-memory and on-disk state divergent.</summary>
    public Exception RollbackException { get; }
}
