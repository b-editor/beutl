using Beutl.Media;

namespace Beutl.Services;

// A project loaded into the application, with the state that belongs to it rather than to the editor.
// ProjectSessionRegistry makes and unloads sessions; while one is loaded its project is a child of the application.
internal sealed class ProjectSession
{
    internal ProjectSession(Project project, FontManager projectFonts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectFonts);
        Project = project;
        ProjectFonts = projectFonts;
    }

    public Project Project { get; }

    // The project file, read from the project's Uri so that it follows a Save As; null for a project not in a file.
    public string? FilePath => Project.Uri is { IsFile: true } uri ? uri.LocalPath : null;

    // A fork of the host's fonts with the project's bundled fonts. The editors of the project's scenes fork it in turn.
    public FontManager ProjectFonts { get; }

    public bool IsAt(string file)
        => FilePath is { } path && FilePathComparison.AreSameCanonicalPath(path, file);
}
