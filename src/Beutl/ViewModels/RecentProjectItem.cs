using Avalonia.Media.Imaging;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Language;
using Beutl.Services;
using FluentIcons.Common;
using Reactive.Bindings;

namespace Beutl.ViewModels;

// An entry of the start page's recent projects. What comes from disk arrives later, since a project can sit on a slow
// or disconnected drive. Each entry reads on its own, so a stalled drive holds up only its own entries.
public sealed class RecentProjectItem : IDisposable
{
    private readonly CancellationTokenSource _disposal = new();

    public RecentProjectItem(string fullName)
    {
        FullName = fullName;
        Name = Path.GetFileNameWithoutExtension(fullName);
        DirectoryName = Path.GetDirectoryName(fullName);
    }

    internal RecentProjectItem(
        Guid repositoryId,
        string name,
        DateTimeOffset updatedAt,
        DateTimeOffset? thumbnailUpdatedAt = null)
    {
        FullName = string.Empty;
        Name = name;
        UnclonedRepositoryId = repositoryId;
        ThumbnailUpdatedAt = thumbnailUpdatedAt;
        RepositoryLabel.Value = Strings.HostedGit_CloudProject;
        Status.Value = RelativeTimeText.Format(updatedAt, DateTimeOffset.Now);
        ShowRemotePlaceholder.Value = !thumbnailUpdatedAt.HasValue;
    }

    public string FullName { get; }

    public string Name { get; }

    public string? DirectoryName { get; }

    internal Guid? UnclonedRepositoryId { get; }

    internal DateTimeOffset? ThumbnailUpdatedAt { get; }

    internal bool IsRemoteOnly => UnclonedRepositoryId.HasValue;

    internal string OpeningKey => UnclonedRepositoryId is { } id ? $"beutl://repo/open?id={id:D}" : FullName;

    internal string ToolTipText => IsRemoteOnly ? Name : FullName;

    internal Icon RepositoryIcon => IsRemoteOnly ? Icon.Cloud : Icon.HardDrive;

    internal string OpenLabel => IsRemoteOnly ? Strings.HostedGit_Clone : Strings.Open;

    internal ReactivePropertySlim<string?> RepositoryLabel { get; } = new();

    public ReactivePropertySlim<Bitmap?> Thumbnail { get; } = new();

    // False until the entry is read, so a project with a picture does not flash the placeholder first.
    public ReactivePropertySlim<bool> HasNoThumbnail { get; } = new();

    public ReactivePropertySlim<bool> ShowRemotePlaceholder { get; } = new();

    public ReactivePropertySlim<string?> Status { get; } = new();

    public ReactivePropertySlim<bool> IsMissing { get; } = new();

    public async Task LoadAsync()
    {
        CancellationToken token = _disposal.Token;
        (Bitmap? Thumbnail, DateTime? LastEdited) read = await Task.Run(() => Read(FullName));

        if (token.IsCancellationRequested)
        {
            read.Thumbnail?.Dispose();
            return;
        }

        Thumbnail.Value = read.Thumbnail;
        HasNoThumbnail.Value = read.Thumbnail is null;
        IsMissing.Value = read.LastEdited is null;
        Status.Value = read.LastEdited is { } lastEdited
            ? RelativeTimeText.Format(new DateTimeOffset(lastEdited), DateTimeOffset.Now)
            : MessageStrings.FileDoesNotExist;
    }

    internal async Task LoadRemoteThumbnailAsync(
        HostedGitRepositoryService service,
        AuthenticatedUser owner,
        SemaphoreSlim? concurrencyGate = null)
    {
        if (UnclonedRepositoryId is not { } repositoryId || !ThumbnailUpdatedAt.HasValue)
            return;

        CancellationToken token = _disposal.Token;
        bool entered = false;
        try
        {
            if (concurrencyGate is not null)
            {
                await concurrencyGate.WaitAsync(token);
                entered = true;
            }

            byte[]? bytes = await service.DownloadThumbnailAsync(repositoryId, owner, token);
            Bitmap? bitmap = bytes is { Length: > 0 }
                ? await Task.Run(() => ReadBitmap(bytes), token)
                : null;

            if (token.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }

            Thumbnail.Value = bitmap;
            ShowRemotePlaceholder.Value = bitmap is null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                ShowRemotePlaceholder.Value = true;
        }
        finally
        {
            if (entered)
                concurrencyGate!.Release();
        }
    }

    public void Dispose()
    {
        _disposal.Cancel();
        _disposal.Dispose();

        // The image lets go of it first, so it never draws a disposed bitmap.
        Bitmap? thumbnail = Thumbnail.Value;
        Thumbnail.Value = null;
        thumbnail?.Dispose();
    }

    private static (Bitmap? Thumbnail, DateTime? LastEdited) Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists)
                return (null, null);

            var thumbnail = new FileInfo(ProjectThumbnailService.GetPath(file));
            if (!thumbnail.Exists)
                return (null, info.LastWriteTimeUtc);

            // Saving a scene leaves the project file alone, while closing the project renews the picture.
            DateTime lastEdited = info.LastWriteTimeUtc > thumbnail.LastWriteTimeUtc
                ? info.LastWriteTimeUtc
                : thumbnail.LastWriteTimeUtc;
            return (ReadBitmap(thumbnail.FullName), lastEdited);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }

    private static Bitmap? ReadBitmap(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A damaged picture is the same as none.
            return null;
        }
    }

    private static Bitmap? ReadBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A damaged picture is the same as none.
            return null;
        }
    }
}
