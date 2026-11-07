using System.Collections.Immutable;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// Gives the color pickers the open project's palettes and writes their edits into the project file at once,
// as a change of the project's channel layout is: the project file has no unsaved state of its own.
internal sealed class ProjectColorPaletteHost : IProjectColorPaletteHost, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<ProjectColorPaletteHost>();
    private readonly Func<IProjectFileWriteLease?> _beginWrite;
    private readonly Action<Project, Uri> _storeProject;
    private readonly IDisposable _projectSubscription;
    private IDisposable? _palettesSubscription;
    private Project? _project;

    public ProjectColorPaletteHost(ProjectService projectService, EditorService editorService)
        : this(projectService.CurrentProject, editorService.TryBeginProjectFileWrite, StoreProjectFile)
    {
    }

    // Writes the project file alone. The default mode also writes the scenes it refers to, which would save their
    // unsaved edits behind the user's back and make each palette edit as slow as saving the whole project.
    internal static void StoreProjectFile(Project project, Uri uri)
    {
        CoreSerializer.StoreToUri(project, uri, CoreSerializationMode.Write);
    }

    // A test seam for the project to follow, the write admission and the write itself.
    internal ProjectColorPaletteHost(
        IObservable<Project?> currentProject,
        Func<IProjectFileWriteLease?> beginWrite,
        Action<Project, Uri> storeProject)
    {
        _beginWrite = beginWrite;
        _storeProject = storeProject;
        _projectSubscription = currentProject.Subscribe(OnProjectChanged);
    }

    public ImmutableArray<ColorPalette>? Palettes => _project?.ColorPalettes.Value;

    public event EventHandler? PalettesChanged;

    public bool TrySetPalettes(ImmutableArray<ColorPalette> palettes)
    {
        if (_project is not { Uri: { } uri } project)
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

        ImmutableArray<ColorPalette> previous = project.ColorPalettes.Value;
        try
        {
            project.ColorPalettes.Value = palettes;
            ProjectPersistence.PersistOrRollback(
                () => _storeProject(project, uri),
                () => project.ColorPalettes.Value = previous);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write the project's color palettes.");
            NotificationService.ShowError(Strings.ColorPalette_ProjectPalettes, MessageStrings.OperationFailed);
            return false;
        }
    }

    public void Dispose()
    {
        _projectSubscription.Dispose();
        _palettesSubscription?.Dispose();
        _palettesSubscription = null;
        _project = null;
    }

    private void OnProjectChanged(Project? project)
    {
        _palettesSubscription?.Dispose();
        _palettesSubscription = null;
        _project = project;
        if (project != null)
        {
            // The first value is the palettes the project opened with, which also announces the project.
            _palettesSubscription = project.ColorPalettes.GetObservable()
                .Subscribe(_ => PalettesChanged?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            PalettesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
