namespace Beutl.Services;

/// <summary>
/// Thrown when a project persist failed and the rollback also failed, leaving in-memory and on-disk
/// state divergent. Callers should ask the user to reopen the project rather than treat it as an
/// ordinary save failure.
/// </summary>
/// <remarks>
/// <see cref="Exception.InnerException"/> is the original persist failure;
/// <see cref="RollbackException"/> is the rollback failure.
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

    /// <summary>The rollback failure that left the state divergent.</summary>
    public Exception RollbackException { get; }
}
