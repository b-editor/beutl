using System.Collections.Immutable;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// Gives the color pickers the open project's palettes and writes their edits at once to the project's palette file
// (ProjectColorPaletteFile), which keeps them out of the project file: that file holds only what the project renders.
internal sealed class ProjectColorPaletteHost : IProjectColorPaletteHost, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<ProjectColorPaletteHost>();
    private readonly Func<IProjectFileWriteLease?> _beginWrite;
    private readonly Action<string, ImmutableArray<ColorPalette>> _store;
    private readonly IDisposable _projectSubscription;
    // The open project's palette file, or null without a project.
    private string? _path;
    private ImmutableArray<ColorPalette> _palettes = [];

    public ProjectColorPaletteHost(ProjectService projectService, EditorService editorService)
        : this(projectService.CurrentProject, editorService.TryBeginProjectFileWrite, ProjectColorPaletteFile.Write)
    {
    }

    // A test seam for the project to follow, the write admission and the write itself.
    internal ProjectColorPaletteHost(
        IObservable<Project?> currentProject,
        Func<IProjectFileWriteLease?> beginWrite,
        Action<string, ImmutableArray<ColorPalette>> store)
    {
        _beginWrite = beginWrite;
        _store = store;
        _projectSubscription = currentProject.Subscribe(OnProjectChanged);
    }

    public ImmutableArray<ColorPalette>? Palettes => _path != null ? _palettes : null;

    public event EventHandler? PalettesChanged;

    public bool TrySetPalettes(ImmutableArray<ColorPalette> palettes)
    {
        if (_path is not { } path)
        {
            return false;
        }

        // Taken right before the write, so that it cannot land on a tree a branch switch, pull or restore is replacing.
        using IProjectFileWriteLease? lease = _beginWrite();
        if (lease is null)
        {
            NotificationService.ShowWarning(Strings.ColorPalette_ProjectPalettes, Strings.FileBrowser_WorkspaceBusy);
            return false;
        }

        // Checked again under the lease, as a merge or a later version may have changed the file since it was read.
        // A file this version cannot read stays as it is, for that version or the user to deal with.
        ProjectColorPaletteFile.Read(path, out bool canWrite);
        if (!canWrite)
        {
            NotificationService.ShowWarning(Strings.ColorPalette_ProjectPalettes, MessageStrings.OperationFailed);
            return false;
        }

        try
        {
            _store(path, palettes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write the project's color palettes.");
            NotificationService.ShowError(Strings.ColorPalette_ProjectPalettes, MessageStrings.OperationFailed);
            return false;
        }

        _palettes = palettes;
        PalettesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        _projectSubscription.Dispose();
        _path = null;
        _palettes = [];
    }

    // A pull, restore or branch switch reopens the project, and with it this read.
    private void OnProjectChanged(Project? project)
    {
        _path = project?.Uri is { IsFile: true } uri ? ProjectColorPaletteFile.GetPath(uri.LocalPath) : null;
        _palettes = [];
        if (_path != null)
        {
            _palettes = ProjectColorPaletteFile.Read(_path, out bool canWrite);
            if (!canWrite)
            {
                _logger.LogWarning("The project's color palette file {Path} cannot be read, so it is left as it is.", _path);
            }
        }

        PalettesChanged?.Invoke(this, EventArgs.Empty);
    }
}
