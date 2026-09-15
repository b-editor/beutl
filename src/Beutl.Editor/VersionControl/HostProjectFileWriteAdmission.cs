namespace Beutl.Editor.VersionControl;

/// <summary>
/// The host-owned admission a project-file writer consults before it touches the project tree.
/// </summary>
/// <remarks>
/// The host's composition root installs it once, so a writer reaches it without going through the
/// editor context it belongs to. <see cref="IProjectFileWriteAdmission"/> is internal, and only the
/// host's own editor context can serve it through <see cref="IServiceProvider"/>; a tool tab attached
/// to an out-of-tree editor context would find nothing there, and treating that absence as permission
/// is exactly the write during a branch switch, pull, or restore that the admission exists to prevent.
/// A writer that finds nothing installed here must refuse as well.
/// </remarks>
internal static class HostProjectFileWriteAdmission
{
    private static IProjectFileWriteAdmission? s_current;

    /// <summary>The admission the host installed, or <see langword="null"/> while no host has.</summary>
    public static IProjectFileWriteAdmission? Current
    {
        get => Volatile.Read(ref s_current);
        set => Volatile.Write(ref s_current, value);
    }
}
