using System.Net.Http.Headers;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Refit;

namespace Beutl.ViewModels.Tools;

internal sealed record StorageActionContext(AuthenticatedUser User, string? FolderId, long Version, CloudStorageItem[] Items);

internal sealed partial class CloudStorageViewModel
{
    private CancellationTokenSource? _mutation;
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> ShowBackgroundProgress { get; } = new();
    public ReactivePropertySlim<string?> ActionError { get; } = new();
    public ReactivePropertySlim<CloudStorageItem?> DetailsItem { get; } = new();

    internal StorageActionContext? CaptureActionContext(IEnumerable<CloudStorageItem> items)
    {
        var selected = items.ToArray();
        if (_disposed || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)
            || !HasListing.Value || IsLoading.Value || IsBusy.Value || selected.Any(item => !Items.Contains(item))) return null;
        return new(user, _folderId, _version, selected);
    }

    internal bool IsActionCurrent(StorageActionContext context) => !_disposed && !IsBusy.Value
        && ReferenceEquals(_owner, context.User) && ReferenceEquals(_clients.AuthenticatedUser.Value, context.User)
        && _version == context.Version && _folderId == context.FolderId && context.Items.All(Items.Contains);

    internal static bool IsValidName(string name) => name.Trim().Length is > 0 and <= 255
        && !name.Any(c => c < 32 || c == 127);

    internal Task<bool> CreateFolderAsync(StorageActionContext context, string name)
    {
        if (!IsValidName(name)) return Task.FromResult(false);
        return MutateAsync(context, async (authorization, token) =>
            await _clients.Storage.CreateFolder(authorization, new { name = name.Trim(), parentId = context.FolderId }, token));
    }

    internal Task<bool> RenameAsync(StorageActionContext context, string name)
    {
        if (context.Items is not [var item] || !item.Can("rename") || !IsValidName(name)) return Task.FromResult(false);
        return MutateAsync(context, (authorization, token) => item.IsFolder
            ? _clients.Storage.UpdateFolder(authorization, item.Id, new { name = name.Trim() }, token)
            : _clients.Storage.UpdateFile(authorization, item.Id, new { name = name.Trim() }, token));
    }

    internal Task<bool> MoveAsync(StorageActionContext context, string? parentId)
    {
        if (context.Items.Length == 0 || context.Items.Any(item => !item.Can("move"))) return Task.FromResult(false);
        if (context.Items is [var folder] && folder.IsFolder)
            return MutateAsync(context, (authorization, token) => _clients.Storage.UpdateFolder(authorization, folder.Id, new { parentId }, token));
        if (context.Items.Any(item => item.IsFolder) || context.Items.Length > 200) return Task.FromResult(false);
        return MutateAsync(context, async (authorization, token) =>
            await _clients.Storage.FileBatch(authorization, new { operation = "move", ids = context.Items.Select(x => x.Id).ToArray(), parentId }, token));
    }

    internal Task<bool> SetVisibilityAsync(StorageActionContext context, bool makePublic)
    {
        string action = makePublic ? "setPublic" : "setPrivate";
        if (context.Items.Length is 0 or > 200 || context.Items.Any(item => item.IsFolder || !item.Can(action)))
            return Task.FromResult(false);
        var ids = context.Items.Select(x => x.Id).ToArray();
        return MutateAsync(context, async (authorization, token) =>
            await _clients.Storage.FileBatch(authorization, new { operation = "visibility", ids, visibility = makePublic ? "PUBLIC" : "PRIVATE" }, token));
    }

    internal Task<bool> DeleteAsync(StorageActionContext context)
    {
        if (context.Items is [var folder] && folder.IsFolder && folder.Can("delete"))
            return MutateAsync(context, async (authorization, token) =>
                await _clients.Storage.DeleteFolderTree(authorization, folder.Id, token));
        var ids = context.Items.Where(item => !item.IsFolder && item.Can("delete")).Select(x => x.Id).ToArray();
        if (ids.Length is 0 or > 200) return Task.FromResult(false);
        return MutateAsync(context with { Items = context.Items.Where(item => !item.IsFolder && item.Can("delete")).ToArray() }, async (authorization, token) =>
            await _clients.Storage.FileBatch(authorization, new { operation = "delete", ids }, token));
    }

    private async Task<bool> MutateAsync(StorageActionContext context, Func<string, CancellationToken, Task> send)
    {
        if (!IsActionCurrent(context)) return false;
        using var progress = new StorageItemOperation(context.Items);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _mutation = operation;
        IsBusy.Value = true;
        ShowBackgroundProgress.Value = !progress.HasItems;
        ActionError.Value = null;
        _load?.Cancel();
        _load = null;
        IsLoadingMore.Value = false;
        IsLoadingMoreVisible.Value = false;
        ++_version;
        ClearFolderCache();
        bool succeeded = false;
        try
        {
            await _clients.SendAuthenticatedAsync(async (authorization, token) => { await send(authorization, token); return true; }, operation.Token, context.User);
            succeeded = true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed && ReferenceEquals(_owner, context.User) && ReferenceEquals(_clients.AuthenticatedUser.Value, context.User))
            {
                _logger.LogError(ex, "Storage operation failed.");
                ActionError.Value = ActionErrorMessage(ex);
            }
        }
        finally
        {
            if (ReferenceEquals(_mutation, operation))
            {
                _mutation = null;
                if (!_disposed) IsBusy.Value = ShowBackgroundProgress.Value = false;
            }
        }
        // Refresh even after an unsuccessful response: a connection can fail after the server
        // commits, and a large folder deletion can finish some batches before finding an in-use file.
        if (!_disposed && ReferenceEquals(_owner, context.User) && ReferenceEquals(_clients.AuthenticatedUser.Value, context.User))
            await Task.WhenAll(LoadAsync(), LoadUsageAsync());
        return succeeded && !_disposed && ReferenceEquals(_owner, context.User) && ReferenceEquals(_clients.AuthenticatedUser.Value, context.User);
    }

    internal async Task<StorageFolderDetailsResponse?> GetFolderDetailsAsync(StorageActionContext context, string id)
    {
        if (!IsActionCurrent(context)) return null;
        using var progress = new StorageItemOperation(context.Items);
        var result = await _clients.SendAuthenticatedAsync((authorization, token) => _clients.Storage.GetFolder(authorization, id, token), _lifetime.Token, context.User);
        return IsActionCurrent(context) ? result.Value : null;
    }

    internal async Task<StorageResponse?> GetFolderChoicesAsync(StorageActionContext context, string? parentId, string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (!IsActionCurrent(context)) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var result = await _clients.SendAuthenticatedAsync((authorization, token) => _clients.Storage.GetEntries(authorization, token, parentId, cursor, "folder"), linked.Token, context.User);
        return IsActionCurrent(context) ? result.Value : null;
    }

    internal async Task<Uri?> GetContentUriAsync(StorageActionContext context, bool publicOnly = false)
    {
        if (!IsActionCurrent(context) || context.Items is not [var item] || item.IsFolder) return null;
        using var progress = new StorageItemOperation(context.Items);
        var result = await _clients.SendAuthenticatedAsync((authorization, token) => _clients.Storage.GetFile(authorization, item.Id, token), _lifetime.Token, context.User);
        if (publicOnly && !result.Value.Actions.Contains("copyLink", StringComparer.Ordinal)) return null;
        return IsActionCurrent(context) && Uri.TryCreate(result.Value.ContentUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http" ? uri : null;
    }

    internal async Task<bool> DownloadAsync(StorageActionContext context, Stream destination, CancellationToken cancellationToken)
    {
        if (!IsActionCurrent(context) || context.Items is not [var item] || !item.Can("download")) return false;
        return await TransferAsync(context, async (token, progress) =>
        {
            await _clients.SendAuthenticatedAsync(async (authorization, ct) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v3/storage/files/{Uri.EscapeDataString(item.Id)}/content");
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
                using var response = await _clients.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await CopyDownloadAsync(response.Content, destination, item.Entry?.Size, value => progress.Report(item, value), ct);
                return true;
            }, token, context.User);
        }, refresh: false, cancellationToken);
    }

    internal void ReportActionError(Exception exception) => ActionError.Value = ActionErrorMessage(exception);

    private static string ActionErrorMessage(Exception exception)
    {
        if (exception is ApiException { Content: { } content })
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("error_code", out var element) || element.ValueKind != JsonValueKind.String)
                    return Strings.CloudStorageActionFailed;
                string? code = element.GetString();
                return code switch
                {
                    "storageFileInUse" => Strings.CloudStorageFileInUse,
                    "storageFolderInUse" => Strings.CloudStorageFolderInUse,
                    "storageInvalidMove" => Strings.CloudStorageInvalidMove,
                    "storageFileNotFound" or "storageFolderNotFound" => Strings.CloudStorageItemNotFound,
                    "invalidRequestBody" => Strings.CloudStorageInvalidRequest,
                    "insufficientStorageSpace" or "tooManyFiles" => Strings.CloudStorageQuotaExceeded,
                    _ => Strings.CloudStorageActionFailed,
                };
            }
            catch (JsonException) { return Strings.CloudStorageActionFailed; }
        }
        return Strings.CloudStorageActionFailed;
    }

    private void CancelActions()
    {
        _transfer?.Cancel();
        _transfer = null;
        IsTransferring.Value = false;
        _mutation?.Cancel();
        _mutation = null;
        IsBusy.Value = false;
        ShowBackgroundProgress.Value = HasItemProgress.Value = false;
        ActionError.Value = null;
        DetailsItem.Value = null;
    }
}
