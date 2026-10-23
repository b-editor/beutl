using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed partial class FileBrowserTabViewModel
{
    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file {Path}", path);
            NotificationService.ShowError(Strings.Open, MessageStrings.OperationFailed);
        }
    }

    public async Task OpenInFileManagerAsync(FileSystemItemViewModel item)
    {
        string path = item.FullPath;
        if (item.IsDirectory ? !Directory.Exists(path) : !File.Exists(path))
        {
            NotificationService.ShowError(FileManagerLauncher.MenuHeader, MessageStrings.FileDoesNotExist);
            return;
        }

        try
        {
            if (!await LaunchFileManagerAsync(FileManagerLauncher.CreateStartInfo(path, item.IsDirectory)))
            {
                _logger.LogWarning("File manager exited unsuccessfully for {Path}", path);
                NotificationService.ShowError(FileManagerLauncher.MenuHeader, MessageStrings.OperationFailed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open {Path} in file manager", path);
            NotificationService.ShowError(FileManagerLauncher.MenuHeader, MessageStrings.OperationFailed);
        }
    }

    private bool CanMutateItem(FileSystemItemViewModel item)
    {
        if (_disposed) return false;
        try
        {
            bool inUse = _editorContext.GetService<IEditorFileUsage>() is { } usage
                ? usage.IsFileInUse(item.FullPath, item.IsDirectory)
                : _editorContext.Object.Uri is { IsFile: true } own && (item.IsDirectory
                    ? FilePathComparison.IsSameOrDescendant(item.FullPath, own.LocalPath)
                    : FilePathComparison.AreSameCanonicalPath(item.FullPath, own.LocalPath));
            if (!inUse) return true;
            NotificationService.ShowError(Strings.File, MessageStrings.FileInUse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify whether {Path} is in use.", item.FullPath);
            NotificationService.ShowError(Strings.File, MessageStrings.OperationFailed);
        }
        return false;
    }

    public async Task DeleteItemAsync(FileSystemItemViewModel item)
    {
        if (!CanMutateItem(item)) return;
        var dialog = CreateDeleteConfirmation(string.Format(MessageStrings.ConfirmDeleteFile, item.Name.Value));

        FAContentDialogResult result = await ConfirmAsync(dialog);
        if (result == FAContentDialogResult.Primary)
        {
            if (!CanMutateItem(item)) return;
            if (!TryBeginProjectFileWrite(Strings.Delete, out IProjectFileWriteLease? fileWrite))
                return;

            using (fileWrite)
                try
                {
                    DeleteEntry(item);
                }
                catch (Exception ex)
                {
                    ReportDeleteFailure(ex, item);
                }
        }
    }

    public async Task DeleteItemsAsync(IReadOnlyList<FileSystemItemViewModel> items)
    {
        if (items.Any(item => !CanMutateItem(item))) return;
        if (items.Count == 0)
            return;

        if (items.Count == 1)
        {
            await DeleteItemAsync(items[0]);
            return;
        }

        var dialog = CreateDeleteConfirmation(string.Format(Strings.DeleteSelectedItems, items.Count));

        FAContentDialogResult result = await ConfirmAsync(dialog);
        if (result == FAContentDialogResult.Primary)
        {
            if (!TryBeginProjectFileWrite(Strings.Delete, out IProjectFileWriteLease? fileWrite))
                return;

            using (fileWrite)
                foreach (var item in items)
                {
                    try
                    {
                        if (!CanMutateItem(item)) continue;
                        DeleteEntry(item);
                    }
                    catch (Exception ex)
                    {
                        ReportDeleteFailure(ex, item);
                    }
                }
        }
    }

    private static FAContentDialog CreateDeleteConfirmation(string content)
    {
        return new FAContentDialog
        {
            Title = Strings.Delete,
            Content = content,
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = FAContentDialogButton.Close
        };
    }

    private static void DeleteEntry(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
        {
            Directory.Delete(item.FullPath, true);
        }
        else
        {
            File.Delete(item.FullPath);
        }
    }

    private void ReportDeleteFailure(Exception ex, FileSystemItemViewModel item)
    {
        _logger.LogError(ex, "Failed to delete {Path}", item.FullPath);
        NotificationService.ShowError(Strings.Delete, MessageStrings.OperationFailed);
    }

    public void CreateNewFolder()
    {
        if (string.IsNullOrEmpty(RootPath.Value) || !Directory.Exists(RootPath.Value))
            return;

        string baseName = Strings.NewFolder;
        string newFolderPath = Path.Combine(RootPath.Value, baseName);
        int counter = 1;

        while (Directory.Exists(newFolderPath))
        {
            newFolderPath = Path.Combine(RootPath.Value, $"{baseName} ({counter})");
            counter++;
        }

        if (!TryBeginProjectFileWrite(Strings.NewFolder, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            try
            {
                Directory.CreateDirectory(newFolderPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create folder at {Path}", newFolderPath);
                NotificationService.ShowError(Strings.NewFolder, MessageStrings.OperationFailed);
            }
    }

    public async Task RenameItemAsync(FileSystemItemViewModel item, string newName)
    {
        if (!CanMutateItem(item)) return;
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name.Value)
            return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            var dialog = new FAContentDialog
            {
                Title = Strings.Error,
                Content = string.Format(MessageStrings.RenameConflict, item.Name.Value, newName),
                CloseButtonText = Strings.Close
            };
            await dialog.ShowAsync();
            return;
        }

        if (!TryBeginProjectFileWrite(Strings.Rename, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            try
            {
                if (item.IsDirectory)
                {
                    Directory.Move(item.FullPath, newPath);
                }
                else
                {
                    File.Move(item.FullPath, newPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename {OldPath} to {NewPath}", item.FullPath, newPath);
                NotificationService.ShowError(Strings.Rename, MessageStrings.OperationFailed);
            }
    }

    /// <summary>
    /// Copies a file. A test replaces it to hold a copy until the test releases it.
    /// </summary>
    internal Action<string, string> CopyFile { get; set; } = static (source, destination) => File.Copy(source, destination);

    /// <summary>
    /// How long a copy or move runs before a notification offers to cancel it.
    /// </summary>
    internal TimeSpan TransferNotificationDelay { get; set; } = TimeSpan.FromSeconds(1);

    public Task TransferFilesToDirectoryAsync(
        IEnumerable<(string LocalPath, bool IsDirectory)> files, string targetDir, bool move)
    {
        return TransferFilesAsync(move, files, targetDir, () => targetDir);
    }

    // Runs on the thread pool. A failure is logged and counted, so the caller reports it on the UI thread.
    private int CopyFilesToDirectoryCore(
        IReadOnlyList<(string LocalPath, bool IsDirectory)> files,
        string targetDir,
        IReadOnlySet<string> merging,
        CancellationToken cancellationToken)
    {
        int failures = 0;
        foreach (var (localPath, isDir) in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            string destPath = Path.Combine(targetDir, Path.GetFileName(localPath));

            try
            {
                if (!isDir)
                {
                    if (!File.Exists(destPath))
                    {
                        CopyFile(localPath, destPath);
                    }
                }
                else if (Directory.Exists(localPath))
                {
                    // A folder that is already there is copied into only when the user chose to merge.
                    if (!Directory.Exists(destPath) || merging.Contains(localPath))
                    {
                        int skippedLinks = FileCopyService.CopyDirectoryRecursive(localPath, destPath, CopyFile, cancellationToken);
                        if (skippedLinks > 0)
                        {
                            _logger.LogWarning("Skipped {Count} folder links in {Source}", skippedLinks, localPath);
                            failures += skippedLinks;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy {Source} to {Dest}", localPath, destPath);
                failures++;
            }
        }

        return failures;
    }

    public Task TransferFilesToResourcesAsync(IEnumerable<(string LocalPath, bool IsDirectory)> files, bool move)
    {
        if (string.IsNullOrEmpty(_projectDirectory))
            return Task.CompletedTask;

        string projectDirectory = _projectDirectory;
        return TransferFilesAsync(
            move, files, Path.Combine(projectDirectory, ResourcesDirectoryName), () => EnsureResourcesDirectory(projectDirectory));
    }

    // Runs on the thread pool. A failure is logged and counted, so the caller reports it on the UI thread.
    private int MoveFilesToDirectoryCore(
        IReadOnlyList<(string LocalPath, bool IsDirectory)> files,
        string targetDir,
        List<string> notMoved,
        CancellationToken cancellationToken)
    {
        string normalizedTargetDir = Path.GetFullPath(targetDir);
        // バッチ全体で1つのコンテキストを使い、正規化で読むディレクトリ一覧を使い回す
        FilePathComparison.ResolutionContext paths = FilePathComparison.CreateResolutionContext();
        string? canonicalTargetDir = null;
        int failures = 0;

        foreach (var (localPath, isDir) in files)
        {
            // A move across volumes copies the whole entry first, so the batch stops between entries.
            if (cancellationToken.IsCancellationRequested)
                break;

            string normalizedSource = Path.GetFullPath(localPath);
            string destPath = Path.Combine(normalizedTargetDir, Path.GetFileName(normalizedSource));

            try
            {
                canonicalTargetDir ??= paths.ResolveCanonicalPath(normalizedTargetDir);

                // 同じ親ディレクトリ内での自己ドロップはスキップ（大文字小文字の区別はファイルシステムに従う）
                string? sourceParent = Path.GetDirectoryName(normalizedSource);
                if (sourceParent != null
                    && string.Equals(paths.ResolveCanonicalPath(sourceParent), canonicalTargetDir, StringComparison.Ordinal))
                {
                    continue;
                }

                // ディレクトリを自身または子孫に移動することはできない。
                // Directory.Move はリンクそのものを移動するので、リンクはリンク先の中にも移動できる
                if (isDir
                    && new DirectoryInfo(normalizedSource).LinkTarget == null
                    && FilePathComparison.IsSameOrDescendantCanonicalPath(
                        paths.ResolveCanonicalPath(normalizedSource), canonicalTargetDir))
                {
                    _logger.LogError("Cannot move {Source} into itself or a descendant directory.", normalizedSource);
                    failures++;
                    continue;
                }

                if (!isDir)
                {
                    if (!File.Exists(destPath))
                    {
                        File.Move(normalizedSource, destPath);
                    }
                }
                else if (Directory.Exists(normalizedSource))
                {
                    if (!Directory.Exists(destPath))
                    {
                        Directory.Move(normalizedSource, destPath);
                    }
                    else
                    {
                        notMoved.Add(Path.GetFileName(normalizedSource));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move {Source} to {Dest}", normalizedSource, destPath);
                failures++;
            }
        }

        return failures;
    }

    /// <summary>
    /// Copies or moves dropped files without blocking the UI thread.
    /// </summary>
    /// <remarks>
    /// Footage copied from an external drive can take minutes, so the transfer runs on the thread pool.
    /// It keeps the project-file write reservation until it ends, so no worktree mutation lands on files
    /// it is still writing. A long transfer shows a notification that cancels it before its next entry.
    /// Failures are reported on the UI thread once the transfer is over.
    /// </remarks>
    private async Task TransferFilesAsync(
        bool move,
        IEnumerable<(string LocalPath, bool IsDirectory)> files,
        string plannedTargetDir,
        Func<string> getTargetDirectory)
    {
        string operation = move ? Strings.Move : Strings.Copy;
        string runningMessage = move ? Strings.FileBrowser_MovingFiles : Strings.FileBrowser_CopyingFiles;
        (string LocalPath, bool IsDirectory)[] entries = files.ToArray();
        HashSet<string> merging = [];
        if (!move)
        {
            // Asked before the write starts, as the dialog can stay open, so Cancel copies nothing.
            foreach ((string localPath, bool isDir) in entries)
            {
                string name = Path.GetFileName(localPath);
                if (!isDir || !Directory.Exists(localPath) || !Directory.Exists(Path.Combine(plannedTargetDir, name)))
                    continue;

                FAContentDialogResult answer = await ConfirmAsync(CreateFolderConflictDialog(name));
                if (answer == FAContentDialogResult.None)
                    return;
                if (answer == FAContentDialogResult.Primary)
                    merging.Add(localPath);
            }
        }

        var notMoved = new List<string>();
        Func<IReadOnlyList<(string LocalPath, bool IsDirectory)>, string, CancellationToken, int> transfer = move
            ? (items, target, token) => MoveFilesToDirectoryCore(items, target, notMoved, token)
            : (items, target, token) => CopyFilesToDirectoryCore(items, target, merging, token);
        if (!TryBeginProjectFileWrite(operation, out IProjectFileWriteLease? fileWrite))
            return;

        int failures;
        string? targetDir = null;
        using (fileWrite)
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_transferCancellation.Token))
        {
            Task<int> work = Task.Run(
                () =>
                {
                    targetDir = getTargetDirectory();
                    return transfer(entries, targetDir, cancellation.Token);
                },
                CancellationToken.None);
            try
            {
                failures = await WaitShowingCancellationAsync(work, operation, runningMessage, cancellation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transfer files dropped on the File Browser.");
                failures = 1;
            }
        }

        if (targetDir is not null && !_disposed)
            ReloadTransferredItems(entries, targetDir);

        foreach (string name in notMoved)
            NotificationService.ShowWarning(operation, string.Format(Strings.FileBrowser_FolderNotMoved, name));

        if (failures > 0)
            NotificationService.ShowError(operation, MessageStrings.OperationFailed);
    }

    // Merge copies only the files the folder is missing; Skip leaves it; Cancel copies nothing.
    internal static FAContentDialog CreateFolderConflictDialog(string folderName)
    {
        return new FAContentDialog
        {
            Title = Strings.Copy,
            Content = string.Format(Strings.FileBrowser_FolderExists, folderName),
            PrimaryButtonText = Strings.FileBrowser_MergeFolder,
            SecondaryButtonText = Strings.FileBrowser_SkipFolder,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary
        };
    }

    private async Task<int> WaitShowingCancellationAsync(
        Task<int> work,
        string operation,
        string runningMessage,
        CancellationTokenSource cancellation)
    {
        using (var delayCancellation = new CancellationTokenSource())
        {
            Task delay = Task.Delay(TransferNotificationDelay, delayCancellation.Token);
            if (await Task.WhenAny(work, delay) == work)
            {
                delayCancellation.Cancel();
                return await work;
            }
        }

        using var dismissal = new CancellationTokenSource();
        NotificationService.Show(new Notification(
            operation,
            runningMessage,
            Expiration: Timeout.InfiniteTimeSpan,
            // A file being copied finishes before Cancel takes effect, so the notification stays
            // until the transfer has stopped.
            Actions:
            [
                new NotificationAction(
                    Strings.Cancel,
                    () => CancelTransfer(cancellation),
                    DismissOnInvoke: false),
            ])
        {
            CancellationToken = dismissal.Token,
        });
        try
        {
            return await work;
        }
        finally
        {
            dismissal.Cancel();
        }
    }

    // The listing can show a file the transfer was still writing, with the size and details of the
    // part written so far. The watcher keeps that item when only a project, scene or element file
    // changed, so the transferred items read theirs again; the rest of the listing stays as it is.
    private void ReloadTransferredItems(
        IEnumerable<(string LocalPath, bool IsDirectory)> entries,
        string targetDir)
    {
        string[] destinations = entries
            .Select(entry => Path.GetFullPath(Path.Combine(targetDir, Path.GetFileName(entry.LocalPath))))
            .ToArray();
        IEnumerable<FileSystemItemViewModel> items = IsHomeView.Value
            ? ProjectDirectoryItems.Concat(FavoriteItems)
            : ViewMode.Value == FileBrowserViewMode.Tree ? TreeRootItems : Items;
        foreach (FileSystemItemViewModel item in items)
            item.ReloadDetailsAtOrBelow(destinations);
    }

    private static void CancelTransfer(CancellationTokenSource cancellation)
    {
        // The notification can be clicked after the transfer has finished and released it.
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private const string ResourcesDirectoryName = "resources";

    private static string EnsureResourcesDirectory(string projectDirectory)
    {
        string resourcesDir = Path.Combine(projectDirectory, ResourcesDirectoryName);
        Directory.CreateDirectory(resourcesDir);
        return resourcesDir;
    }

    /// <summary>
    /// Reserves the project workspace for one write, or reports why the write cannot start.
    /// </summary>
    /// <remarks>
    /// Taken right before the write, after any confirmation dialog, so a confirmation that closes
    /// during a branch switch, pull, or restore cannot land its write on the tree Git is replacing.
    /// The admission comes from the host that owns the editor context, not from the context itself: an
    /// out-of-tree editor context cannot serve it, and a context no host owns is refused, not admitted.
    /// </remarks>
    private bool TryBeginProjectFileWrite(
        string operation,
        [NotNullWhen(true)] out IProjectFileWriteLease? fileWrite)
    {
        fileWrite = null;
        if (_disposed)
            return false;

        IProjectFileWriteAdmission? admission = HostProjectFileWriteAdmission.Resolve(_editorContext);
        if (admission is null)
        {
            _logger.LogError(
                "No host owns this editor context, so no write admission governs it; refusing {Operation}.",
                operation);
            NotificationService.ShowError(operation, MessageStrings.OperationFailed);
            return false;
        }

        fileWrite = admission.TryBeginProjectFileWrite();
        if (fileWrite is not null)
            return true;

        NotificationService.ShowWarning(operation, Strings.FileBrowser_WorkspaceBusy);
        return false;
    }
}
