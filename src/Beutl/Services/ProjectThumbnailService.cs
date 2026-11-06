using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Services;

// Keeps the picture the start page shows for a project: the frame on screen when the project closes. The picture
// sits in the project's .beutl folder, which version control and exported packages leave out.
internal sealed class ProjectThumbnailService
{
    private const string FolderName = "thumbnails";
    private const int MaxWidth = 480;
    private const int MaxHeight = 270;

    // A scene too slow to draw keeps its last picture rather than holding up the close.
    private static readonly TimeSpan s_drawLimit = TimeSpan.FromSeconds(2);
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectThumbnailService>();

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;

    public ProjectThumbnailService(ProjectService projectService, EditorService editorService)
    {
        _projectService = projectService;
        _editorService = editorService;
        // The editors are gone by the Closing event.
        _projectService.ClosingPreparing += OnProjectClosingAsync;
    }

    // Named after the project file, as a folder can hold several projects.
    internal static string GetPath(string projectFile)
        => Path.Combine(
            Path.GetDirectoryName(projectFile) ?? string.Empty,
            EditorConstants.BeutlFolder,
            FolderName,
            Path.GetFileName(projectFile) + ".png");

    internal static void Save(Bitmap frame, string projectFile)
    {
        float scale = MathF.Min(1f, MathF.Min((float)MaxWidth / frame.Width, (float)MaxHeight / frame.Height));
        SKImageInfo info = frame.SKBitmap.Info.WithSize(
            Math.Max(1, (int)(frame.Width * scale)),
            Math.Max(1, (int)(frame.Height * scale)));
        SKBitmap scaled = frame.SKBitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("The frame could not be scaled.");
        using var thumbnail = new Bitmap(scaled);

        string path = GetPath(projectFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!thumbnail.Save(path, EncodedImageFormat.Png))
            throw new InvalidOperationException("The thumbnail could not be encoded.");
    }

    // A project deleted on its own leaves its folder behind, and a later project of the same name must not inherit
    // the picture.
    internal static void Delete(string projectFile)
    {
        try
        {
            File.Delete(GetPath(projectFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogDebug(ex, "Failed to delete the thumbnail of {Project}.", projectFile);
        }
    }

    private async Task OnProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken cancellationToken)
    {
        if (closeContext.CloseIntent != ProjectService.ProjectCloseIntent.SaveChanges
            || _editorService.IsWorktreeMutationActive
            || _projectService.CurrentProject.Value is not { Uri: { IsFile: true } projectUri } project
            || FindSceneEditor(project) is not { } editor)
        {
            return;
        }

        Task<Bitmap?> drawing = editor.Player.DrawThumbnailFrame();
        try
        {
            using Bitmap? frame = await drawing.WaitAsync(s_drawLimit, cancellationToken);
            if (frame is not null)
            {
                await Task.Run(() => Save(frame, projectUri.LocalPath), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The project closes either way; it just keeps the picture it had.
            s_logger.LogDebug(ex, "Failed to capture the thumbnail of {Project}.", projectUri.LocalPath);
            if (!drawing.IsCompleted)
                _ = DisposeWhenDrawnAsync(drawing);
        }
    }

    private static async Task DisposeWhenDrawnAsync(Task<Bitmap?> drawing)
    {
        try
        {
            (await drawing)?.Dispose();
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "A thumbnail that outlasted its project's close failed to draw.");
        }
    }

    // A scene removed from the project can still have its tab open, and is no picture of the project.
    private EditViewModel? FindSceneEditor(Project project)
    {
        bool Shows(EditViewModel editor) => !editor.IsDisposingOrDisposed && project.Items.Contains(editor.Scene);

        if (_editorService.SelectedTabItem.Value?.Context.Value is EditViewModel selected && Shows(selected))
            return selected;

        return _editorService.TabItems.Select(item => item.Context.Value)
            .OfType<EditViewModel>()
            .FirstOrDefault(Shows);
    }
}
