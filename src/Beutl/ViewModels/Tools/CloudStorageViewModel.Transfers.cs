using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Refit;

namespace Beutl.ViewModels.Tools;

internal sealed partial class CloudStorageViewModel : IFileBrowserStorageDropTarget
{
    private CancellationTokenSource? _transfer;
    public ReactivePropertySlim<bool> IsTransferring { get; } = new();
    public ReactivePropertySlim<double> TransferProgress { get; } = new();
    public ReactivePropertySlim<bool> TransferIndeterminate { get; } = new();
    public ReactiveCommand CancelTransfer { get; } = new();

    public bool CanDrop(IDataTransfer data, string? destination)
    {
        if (CaptureActionContext([]) == null) return false;
        if (data.TryGetValue(StorageDragData.Format) is { } source)
            return source.PendingLocalPaths == null && source.ProviderId == "beutl" && ReferenceEquals(source.AccountIdentity, _owner) && source.IsCurrent()
                && source.CanMoveTo?.Invoke(destination) != false
                && source.Entries.All(x => !x.IsFolder || x.Id != destination);
        return data.Contains(DataFormat.File);
    }

    public async Task DropAsync(IDataTransfer data, string? destination)
    {
        if (!CanDrop(data, destination)) return;
        if (data.TryGetValue(StorageDragData.Format) is { } source)
        {
            using var consumption = source.Consumption?.Claim();
            if (await source.MoveAsync(destination) && !_disposed && !ReferenceEquals(source.SourceBrowser, this)) await LoadAsync();
        }
        else if (CaptureActionContext([]) is { } context)
        {
            var files = data.TryGetFiles()?.ToArray() ?? [];
            if (files.Length != 0) await UploadStorageItemsAsync(context, files, destination);
        }
    }

    internal string? CurrentFolderId => _folderId;

    internal bool IsTransferCurrent(StorageActionContext context) => !_disposed
        && ReferenceEquals(_owner, context.User) && ReferenceEquals(_clients.AuthenticatedUser.Value, context.User)
        && context.Version == _version && context.FolderId == _folderId;

    internal Task<bool> MoveDroppedEntriesAsync(StorageActionContext context, string? destination)
    {
        if (!IsActionCurrent(context)) return Task.FromResult(false);
        if (context.Items.Length > 200)
        {
            ActionError.Value = Strings.CloudStorageSelectionLimit;
            return Task.FromResult(false);
        }
        if (context.Items.Length == 0 || context.Items.Any(x => !x.Can("move")) || destination == context.FolderId)
            return Task.FromResult(false);
        if (context.Items.Any(x => x.IsFolder && x.Id == destination))
        {
            ActionError.Value = Strings.CloudStorageInvalidMove;
            return Task.FromResult(false);
        }
        return MutateAsync(context, async (authorization, token) => await _clients.Storage.MoveEntries(authorization,
            new { entries = context.Items.Select(x => new { id = x.Id, kind = x.IsFolder ? "folder" : "file" }).ToArray(), parentId = destination }, token));
    }

    public ReactivePropertySlim<bool> HasItemProgress { get; } = new();

    private async Task<bool> TransferAsync(StorageActionContext context, Func<CancellationToken, StorageItemOperation, Task> run, bool refresh, CancellationToken cancellationToken = default)
    {
        if (!IsActionCurrent(context)) return false;
        using var progress = new StorageItemOperation(context.Items);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _transfer = cancellation;
        IsBusy.Value = IsTransferring.Value = true;
        HasItemProgress.Value = progress.HasItems;
        ActionError.Value = null;
        TransferProgress.Value = 0;
        TransferIndeterminate.Value = false;
        bool success = false;
        try
        {
            await run(cancellation.Token, progress);
            success = IsTransferCurrent(context) && !cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsTransferCurrent(context))
            {
                _logger.LogError(ex, "Storage transfer failed.");
                if (ActionError.Value == null) ReportActionError(ex);
            }
        }
        finally
        {
            if (ReferenceEquals(_transfer, cancellation))
            {
                _transfer = null;
                if (!_disposed) IsBusy.Value = IsTransferring.Value = HasItemProgress.Value = false;
            }
        }
        if (refresh && IsTransferCurrent(context))
        {
            ClearFolderCache();
            await Task.WhenAll(LoadAsync(), LoadUsageAsync());
        }
        return success;
    }

    internal Task<bool> UploadStorageItemsAsync(StorageActionContext context, IReadOnlyList<IStorageItem> items, string? destination)
        => TransferAsync(context with { Items = Items.Where(item => item.IsFolder && item.Id == destination).ToArray() }, async (token, _) =>
        {
            var visited = new HashSet<Uri>();
            var created = new UploadRollbackState();
            int count = 0;
            try
            {
                foreach (var item in items) await UploadItem(item, destination);
            }
            catch (Exception ex)
            {
                bool cleaned = await RollBackUploadAsync(context, created);
                if (!cleaned)
                {
                    if (IsTransferCurrent(context))
                    {
                        string? message = ActionError.Value ?? (ex is OperationCanceledException ? null : ActionErrorMessage(ex));
                        ActionError.Value = string.IsNullOrEmpty(message) ? Strings.CloudStorageUploadRollbackIncomplete
                            : message + Environment.NewLine + Strings.CloudStorageUploadRollbackIncomplete;
                    }
                    else NotificationService.ShowWarning(Strings.CloudStorage, Strings.CloudStorageUploadRollbackIncomplete);
                }
                throw;
            }

            async Task UploadItem(IStorageItem item, string? parent)
            {
                token.ThrowIfCancellationRequested();
                if (!IsTransferCurrent(context)) throw new OperationCanceledException(token);
                if (++count > 10000 || !IsValidName(item.Name)) throw new InvalidDataException("Invalid storage upload tree.");
                if (item is IStorageFolder folder)
                {
                    if (!visited.Add(folder.Path) || folder.TryGetLocalPath() is { } path && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidDataException("Linked or cyclic folders cannot be uploaded.");
                    created.UnconfirmedWrites++;
                    var result = await _clients.SendAuthenticatedAsync((auth, ct) => _clients.Storage.CreateFolder(auth, new { name = item.Name, parentId = parent }, ct), token, context.User);
                    string id = result.Value.Id ?? throw new InvalidDataException("Missing folder id.");
                    created.Folders.Add(id);
                    created.UnconfirmedWrites--;
                    await foreach (var child in folder.GetItemsAsync().WithCancellation(token))
                    {
                        using (child) await UploadItem(child, id);
                    }
                }
                else if (item is IStorageFile file)
                {
                    await UploadFileAsync(context, file, parent, created, token);
                }
            }
        }, refresh: true);

    private sealed class UploadRollbackState
    {
        public List<string> Files { get; } = [];
        public List<string> Folders { get; } = [];
        public int UnconfirmedWrites { get; set; }
    }

    private async Task<bool> RollBackUploadAsync(StorageActionContext context, UploadRollbackState created)
    {
        if (created.Files.Count == 0 && created.Folders.Count == 0 && created.UnconfirmedWrites == 0) return true;
        if (!CanCleanUpload(context)) return false;
        if (!_disposed)
        {
            TransferIndeterminate.Value = true;
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool cleaned = created.UnconfirmedWrites == 0;
        foreach (string[] ids in created.Files.Distinct(StringComparer.Ordinal).Chunk(200))
        {
            if (!CanCleanUpload(context) || cleanup.IsCancellationRequested) return false;
            try
            {
                var result = await _clients.SendAuthenticatedAsync((auth, ct) => _clients.Storage.FileBatch(auth, new { operation = "delete", ids }, ct), cleanup.Token, context.User);
                cleaned &= result.Value.Affected == ids.Length;
            }
            catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                // A missing or in-use file rejects the batch. Clean the other known
                // files independently without treating the entire batch as deleted.
                foreach (string id in ids)
                {
                    if (!CanCleanUpload(context) || cleanup.IsCancellationRequested) return false;
                    try { await _clients.SendAuthenticatedAsync(async (auth, ct) => { await _clients.Storage.DeleteFile(auth, id, ct); return true; }, cleanup.Token, context.User); }
                    catch (ApiException missing) when (missing.StatusCode == HttpStatusCode.NotFound) { }
                    catch (Exception error) { cleaned = false; _logger.LogWarning(error, "Could not remove an uploaded file during rollback."); }
                }
            }
            catch (Exception ex) { cleaned = false; _logger.LogWarning(ex, "Could not roll back uploaded files."); }
        }
        foreach (string id in created.Folders.AsEnumerable().Reverse())
        {
            if (!CanCleanUpload(context) || cleanup.IsCancellationRequested) return false;
            try { await _clients.SendAuthenticatedAsync((auth, ct) => _clients.Storage.DeleteEmptyFolder(auth, id, ct), cleanup.Token, context.User); }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            catch (Exception ex) { cleaned = false; _logger.LogWarning(ex, "Could not remove an empty upload folder during rollback."); }
        }
        return cleaned;
    }

    // Closing the browser cancels the upload, but cleanup still belongs to the
    // captured account. Never use a replacement account to remove these IDs.
    private bool CanCleanUpload(StorageActionContext context) => ReferenceEquals(_clients.AuthenticatedUser.Value, context.User);

    private async Task UploadFileAsync(StorageActionContext context, IStorageFile file, string? parent, UploadRollbackState created, CancellationToken token)
    {
        TransferProgress.Value = 0;
        await using var source = await file.OpenReadAsync();
        token.ThrowIfCancellationRequested();
        string? spool = null;
        Stream input = source;
        try
        {
            if (!source.CanSeek)
            {
                spool = Path.GetTempFileName();
                input = new FileStream(spool, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 81920, true);
                await source.CopyToAsync(input, token);
                input.Position = 0;
            }
            long length = input.Length;
            if (length <= 0)
            {
                ActionError.Value = Strings.CloudStorageEmptyUpload;
                throw new InvalidDataException("Empty files cannot be uploaded to this storage service.");
            }
            string uploadId = Guid.NewGuid().ToString();
            bool complete = false;
            try
            {
                var started = await RetryTransferAsync(() => _clients.SendAuthenticatedAsync((auth, ct) => _clients.Storage.StartUpload(auth,
                    new { id = uploadId, name = file.Name, mimeType = AiMediaTypes.Get(file.Name), size = length }, ct), token, context.User), token);
                int partSize = started.Value.PartSize;
                if (started.Value.Id != uploadId || partSize is < 1 or > 16 * 1024 * 1024 || started.Value.PartCount != (length + partSize - 1) / partSize)
                    throw new InvalidDataException("Invalid multipart upload response.");
                var parts = new List<object>();
                var buffer = new byte[partSize];
                long sent = 0;
                for (int number = 1; sent < length; number++)
                {
                    int size = (int)Math.Min(partSize, length - sent);
                    await input.ReadExactlyAsync(buffer.AsMemory(0, size), token);
                    var uploaded = await RetryTransferAsync(() => _clients.SendAuthenticatedAsync(async (auth, ct) =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v3/storage/uploads/{uploadId}/parts/{number}");
                        request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
                        request.Content = new ByteArrayContent(buffer, 0, size);
                        request.Content.Headers.ContentType = new("application/octet-stream");
                        using var response = await _clients.HttpClient.SendAsync(request, ct);
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadFromJsonAsync<StorageUploadPartResponse>(ct) ?? throw new InvalidDataException();
                    }, token, context.User), token);
                    if (uploaded.Value.PartNumber != number || string.IsNullOrEmpty(uploaded.Value.Etag)) throw new InvalidDataException("Invalid upload part response.");
                    parts.Add(new { partNumber = number, etag = uploaded.Value.Etag });
                    sent += size;
                    TransferProgress.Value = 100.0 * sent / length;
                }
                if (await input.ReadAsync(buffer.AsMemory(0, 1), token) != 0) throw new IOException("The upload source changed.");
                created.UnconfirmedWrites++;
                var result = await RetryTransferAsync(() => _clients.SendAuthenticatedAsync((auth, ct) => _clients.Storage.CompleteUpload(auth, uploadId, new { parts }, ct), token, context.User), token);
                complete = true;
                string id = result.Value.Id ?? throw new InvalidDataException("Missing uploaded file id.");
                created.Files.Add(id);
                created.UnconfirmedWrites--;
                if (parent != null)
                    await _clients.SendAuthenticatedAsync(async (auth, ct) => { await _clients.Storage.UpdateFile(auth, id, new { parentId = parent }, ct); return true; }, token, context.User);
            }
            finally
            {
                if (!complete && CanCleanUpload(context))
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await _clients.SendAuthenticatedAsync(async (auth, ct) => { await _clients.Storage.CancelUpload(auth, uploadId, ct); return true; }, cleanup.Token, context.User); }
                    catch (Exception ex) { _logger.LogDebug(ex, "The server will reconcile the unfinished storage upload."); }
                }
            }
        }
        finally
        {
            if (!ReferenceEquals(input, source)) await input.DisposeAsync();
            if (spool != null) File.Delete(spool);
        }
    }

    private static async Task<T> RetryTransferAsync<T>(Func<Task<T>> send, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await send(); }
            catch (Exception ex) when (attempt < 2 && (ex is HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError }
                || ex is ApiException { StatusCode: >= HttpStatusCode.InternalServerError }))
            { await Task.Delay(300 * (attempt + 1), token); }
        }
    }

    internal async Task<string[]?> ExportStorageItemsAsync(StorageActionContext context, string directory)
    {
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The export staging directory already exists.");
        var paths = new List<string>();
        bool success = await TransferAsync(context, async (token, progress) =>
        {
            TransferIndeterminate.Value = true;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var folders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in context.Items)
                paths.Add(await ExportItem(item.Id, item.Name, item.IsFolder, directory, item, item.Entry?.Size));

            async Task<string> ExportItem(string id, string name, bool folder, string parent, CloudStorageItem? visibleItem = null, long? expectedLength = null)
            {
                token.ThrowIfCancellationRequested();
                if (!IsTransferCurrent(context)) throw new OperationCanceledException(token);
                string safe = SafeTransferName(name);
                string path = Path.Combine(parent, safe);
                for (int i = 1; File.Exists(path) || Directory.Exists(path); i++)
                    path = Path.Combine(parent, $"{Path.GetFileNameWithoutExtension(safe)} ({i}){Path.GetExtension(safe)}");
                if (folder)
                {
                    if (!folders.Add(id)) throw new InvalidDataException("Cyclic storage hierarchy.");
                    Directory.CreateDirectory(path);
                    string? cursor = null;
                    var cursors = new HashSet<string>();
                    do
                    {
                        var page = await FetchPageAsync(context.User, id, cursor, token);
                        if (page.ParentId != id) throw new InvalidDataException("Storage folder changed.");
                        foreach (var entry in page.Entries)
                        {
                            if (entry.Kind == "file" && !entry.Actions.Contains("download")) throw new InvalidOperationException("File download is unavailable.");
                            await ExportItem(entry.Id, entry.Name, entry.Kind == "folder", path, expectedLength: entry.Size);
                        }
                        cursor = page.NextCursor;
                        if (cursor != null && !cursors.Add(cursor)) throw new InvalidDataException("Repeated storage cursor.");
                    } while (cursor != null);
                }
                else
                {
                    await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    await _clients.SendAuthenticatedAsync(async (auth, ct) =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/storage/files/{Uri.EscapeDataString(id)}/content");
                        request.Headers.Authorization = AuthenticationHeaderValue.Parse(auth);
                        using var response = await _clients.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();
                        await CopyDownloadAsync(response.Content, output, expectedLength,
                            value => { if (visibleItem != null) progress.Report(visibleItem, value); }, ct);
                        return true;
                    }, token, context.User);
                }
                return path;
            }
        }, refresh: false);
        if (success) return paths.ToArray();
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException ex) { _logger.LogWarning(ex, "Could not remove an incomplete storage download."); }
        catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Could not remove an incomplete storage download."); }
        return null;
    }

    private static async Task CopyDownloadAsync(HttpContent content, Stream destination, long? expectedLength, Action<double?> report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long? metadataLength = expectedLength is > 0 ? expectedLength : null;
        long? headerLength = content.Headers.ContentLength;
        if (metadataLength is { } stored && headerLength is { } declared && stored != declared)
            throw new InvalidDataException($"Content-Length is {declared} bytes, but storage metadata specifies {stored} bytes.");
        long? length = metadataLength ?? headerLength;
        double? previous = length > 0 ? 0 : null;
        report(previous);
        await using var source = await content.ReadAsStreamAsync(token);
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), token)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
                copied += read;
                if (length is > 0)
                {
                    // Completion is reported only after EOF and the final byte-count check.
                    double value = Math.Min(99, 100.0 * copied / length.Value);
                    if (value - previous.GetValueOrDefault() >= 0.5)
                    {
                        report(value);
                        previous = value;
                    }
                }
            }
            if (length is { } advertised && copied != advertised)
                throw new InvalidDataException($"Downloaded {copied} bytes, but expected {advertised} bytes.");
            report(100);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static string SafeTransferName(string name)
    {
        string safe = string.Concat(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safe.Length == 0 || safe is "." or "..") safe = "file";
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(Path.GetFileNameWithoutExtension(safe), StringComparer.OrdinalIgnoreCase)) safe = "_" + safe;
        if (Encoding.UTF8.GetByteCount(safe) <= 200) return safe;
        string extension = Path.GetExtension(safe);
        if (Encoding.UTF8.GetByteCount(extension) > 30) extension = "";
        var shortened = new StringBuilder();
        int bytes = Encoding.UTF8.GetByteCount(extension);
        foreach (var rune in Path.GetFileNameWithoutExtension(safe).EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 200) break;
            shortened.Append(rune);
            bytes += rune.Utf8SequenceLength;
        }
        return shortened + extension;
    }
}
