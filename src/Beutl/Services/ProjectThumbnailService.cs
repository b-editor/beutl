using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.VersionControl;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Services;

// Keeps the picture the start page shows for a project: the frame on screen when the project closes, and on push
// mirrors it to a Beutl-hosted repository. The picture sits in the project's .beutl folder, which version control and
// exported packages leave out.
internal sealed class ProjectThumbnailService
{
    private const string FolderName = "thumbnails";
    private const int MaxWidth = 480;
    private const int MaxHeight = 270;
    private static readonly byte[] s_pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // A scene too slow to draw keeps its last picture rather than holding up the close.
    private static readonly TimeSpan s_drawLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_uploadLimit = TimeSpan.FromSeconds(15);
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectThumbnailService>();

    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private Task _pendingSync = Task.CompletedTask;

    public ProjectThumbnailService(
        ProjectService projectService,
        EditorService editorService,
        VersionControlCoordinator? versionControlCoordinator = null)
    {
        _projectService = projectService;
        _editorService = editorService;
        // The editors are gone by the Closing event.
        _projectService.ClosingPreparing += OnProjectClosingAsync;
        if (versionControlCoordinator is not null)
        {
            versionControlCoordinator.PushSucceeded += OnPushSucceeded;
        }
    }

    // Test seam: lets tests await the background thumbnail upload triggered by a push.
    internal Task PendingSync => Volatile.Read(ref _pendingSync);

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
        using var thumbnail = Bitmap.FromFixedLayout(scaled);

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

        await CaptureAndSaveAsync(editor, projectUri.LocalPath, cancellationToken);
    }

    private void OnPushSucceeded(object? sender, IProjectVersionControlService service)
    {
        if (_projectService.CurrentProject.Value is not { Uri: { IsFile: true } projectUri } project
            || _editorService.ApiApplication is not { AuthenticatedUser.Value: { } owner } api)
        {
            return;
        }

        Task sync = SyncRepositoryThumbnailAsync(project, projectUri.LocalPath, service, api, owner);
        Volatile.Write(ref _pendingSync, sync);
    }

    internal async Task SyncRepositoryThumbnailAsync(
        Project project,
        string projectFile,
        IProjectVersionControlService service,
        BeutlApiApplication api,
        AuthenticatedUser owner)
    {
        using var timeout = new CancellationTokenSource(s_uploadLimit);
        CancellationToken token = timeout.Token;
        try
        {
            string? originUrl = (await service.GetRemotesAsync(token).ConfigureAwait(false)).FirstOrDefault()?.Url;
            (HostedGitRemoteUrl? remote, _) = await HostedGitRemoteUrl.ResolveAsync(originUrl, api, token).ConfigureAwait(false);
            if (remote is null || !Guid.TryParseExact(remote.RepositoryId, "D", out Guid repositoryId))
                return;

            await TryCaptureOpenProjectAsync(project, projectFile, token).ConfigureAwait(false);

            byte[]? pngBytes = await Task.Run(() => TryReadThumbnailBytes(projectFile), token).ConfigureAwait(false);
            if (pngBytes is null)
                return;

            await api.GetResource<HostedGitRepositoryService>()
                .UploadThumbnailAsync(repositoryId, pngBytes, owner, token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Pushing already succeeded; a failed thumbnail sync never blocks or errors the push.
            s_logger.LogDebug(ex, "Failed to sync the hosted repository thumbnail for {Project}.", projectFile);
        }
    }

    private async Task TryCaptureOpenProjectAsync(Project project, string projectFile, CancellationToken cancellationToken)
    {
        EditViewModel? editor = await Dispatcher.UIThread.InvokeAsync(() =>
            !_editorService.IsWorktreeMutationActive
            && ReferenceEquals(_projectService.CurrentProject.Value, project)
                ? FindSceneEditor(project)
                : null);
        if (editor is not null)
        {
            await CaptureAndSaveAsync(editor, projectFile, cancellationToken);
        }
    }

    private static async Task CaptureAndSaveAsync(
        EditViewModel editor,
        string projectFile,
        CancellationToken cancellationToken)
    {
        Task<Bitmap?> drawing = editor.Player.DrawThumbnailFrame();
        try
        {
            using Bitmap? frame = await drawing.WaitAsync(s_drawLimit, cancellationToken);
            if (frame is not null)
            {
                await Task.Run(() => Save(frame, projectFile), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The project closes or pushes either way; it just keeps the picture it had.
            s_logger.LogDebug(ex, "Failed to capture the thumbnail of {Project}.", projectFile);
            if (!drawing.IsCompleted)
                _ = DisposeWhenDrawnAsync(drawing);
        }
    }

    internal static byte[]? TryReadThumbnailBytes(string projectFile)
    {
        try
        {
            string path = GetPath(projectFile);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < s_pngSignature.Length || info.Length > HostedGitRepositoryService.MaxThumbnailBytes)
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            return bytes.AsSpan(0, s_pngSignature.Length).SequenceEqual(s_pngSignature) ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogDebug(ex, "Failed to read the thumbnail of {Project}.", projectFile);
            return null;
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
