namespace Beutl.Services;

/// <summary>
/// Slow project work that replaces the editor area with a progress view, so the user never sees
/// an editor that looks usable but is waiting on it.
/// </summary>
internal enum ProjectLifecycleActivity
{
    None,

    /// <summary>A new project is being created and its first version recorded before it opens.</summary>
    CreatingProject,

    /// <summary>A version-controlled project is saving and recording its version before it closes.</summary>
    ClosingProject,

    /// <summary>
    /// Version control is being enabled for the open project, which is saved and has its first version
    /// recorded while its editors are suspended.
    /// </summary>
    EnablingVersionControl,
}
