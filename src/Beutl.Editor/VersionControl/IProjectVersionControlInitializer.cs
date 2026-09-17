namespace Beutl.Editor.VersionControl;

internal interface IProjectVersionControlInitializer
{
    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<bool> InitializeCurrentProjectAsync(
        Project expectedProject,
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts creating a project that is tracked from its first version, so the project opens only after
    /// that version is recorded.
    /// </summary>
    INewProjectVersionControlSetup BeginNewProject(
        Func<CancellationToken, Task<GitIdentity?>> requestIdentityAsync);
}

/// <summary>
/// Records the first version of a project that is being created, before its editor opens.
/// </summary>
/// <remarks>
/// Dispose it once the creation has finished, whether or not the project opened. A repository prepared for a
/// project that did not open is released then.
/// </remarks>
internal interface INewProjectVersionControlSetup : IAsyncDisposable
{
    /// <summary>
    /// Initializes version control for <paramref name="project"/>, whose files are written but which is not
    /// open yet. When the project then opens in the same creation, it opens tracked.
    /// </summary>
    /// <returns><see langword="true"/> if the project will open tracked.</returns>
    Task<bool> InitializeAsync(Project project, CancellationToken cancellationToken);
}
