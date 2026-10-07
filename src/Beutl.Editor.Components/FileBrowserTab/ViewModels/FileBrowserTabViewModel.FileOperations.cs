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
        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
            return;

        string baseName = Strings.NewFolder;
        string newFolderPath = Path.Combine(_rootPath, baseName);
        int counter = 1;

        while (Directory.Exists(newFolderPath))
        {
            newFolderPath = Path.Combine(_rootPath, $"{baseName} ({counter})");
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

    public void CopyFilesToDirectory(IEnumerable<(string LocalPath, bool IsDirectory)> files, string targetDir)
    {
        if (!TryBeginProjectFileWrite(Strings.Copy, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            CopyFilesToDirectoryCore(files, targetDir);
    }

    private void CopyFilesToDirectoryCore(
        IEnumerable<(string LocalPath, bool IsDirectory)> files,
        string targetDir)
    {
        foreach (var (localPath, isDir) in files)
        {
            string destPath = Path.Combine(targetDir, Path.GetFileName(localPath));

            try
            {
                if (!isDir)
                {
                    if (!File.Exists(destPath))
                    {
                        File.Copy(localPath, destPath);
                    }
                }
                else if (Directory.Exists(localPath))
                {
                    if (!Directory.Exists(destPath))
                    {
                        FileCopyService.CopyDirectoryRecursive(localPath, destPath);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to copy {Source} to {Dest}", localPath, destPath);
                NotificationService.ShowError(Strings.Copy, MessageStrings.OperationFailed);
            }
        }
    }

    public void CopyFilesToResources(IEnumerable<(string LocalPath, bool IsDirectory)> files)
    {
        if (string.IsNullOrEmpty(_projectDirectory))
            return;

        if (!TryBeginProjectFileWrite(Strings.Copy, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            CopyFilesToDirectoryCore(files, EnsureResourcesDirectory(_projectDirectory));
    }

    public void MoveFilesToDirectory(IEnumerable<(string LocalPath, bool IsDirectory)> files, string targetDir)
    {
        if (!TryBeginProjectFileWrite(Strings.Move, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            MoveFilesToDirectoryCore(files, targetDir);
    }

    private void MoveFilesToDirectoryCore(
        IEnumerable<(string LocalPath, bool IsDirectory)> files,
        string targetDir)
    {
        string normalizedTargetDir = Path.GetFullPath(targetDir);

        foreach (var (localPath, isDir) in files)
        {
            string normalizedSource = Path.GetFullPath(localPath);

            // 同じ親ディレクトリ内での自己ドロップはスキップ（大文字小文字の区別はファイルシステムに従う）
            string? sourceParent = Path.GetDirectoryName(normalizedSource);
            if (sourceParent != null
                && FilePathComparison.AreSameCanonicalPath(
                    sourceParent, Path.TrimEndingDirectorySeparator(normalizedTargetDir)))
            {
                continue;
            }

            // ディレクトリを自身または子孫に移動することはできない
            if (isDir && FilePathComparison.IsSameOrDescendant(normalizedSource, normalizedTargetDir))
            {
                _logger.LogError("Cannot move {Source} into itself or a descendant directory.", normalizedSource);
                NotificationService.ShowError(Strings.Move, MessageStrings.OperationFailed);
                continue;
            }

            string destPath = Path.Combine(normalizedTargetDir, Path.GetFileName(normalizedSource));

            try
            {
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move {Source} to {Dest}", normalizedSource, destPath);
                NotificationService.ShowError(Strings.Move, MessageStrings.OperationFailed);
            }
        }
    }

    public void MoveFilesToResources(IEnumerable<(string LocalPath, bool IsDirectory)> files)
    {
        if (string.IsNullOrEmpty(_projectDirectory))
            return;

        if (!TryBeginProjectFileWrite(Strings.Move, out IProjectFileWriteLease? fileWrite))
            return;

        using (fileWrite)
            MoveFilesToDirectoryCore(files, EnsureResourcesDirectory(_projectDirectory));
    }

    private static string EnsureResourcesDirectory(string projectDirectory)
    {
        string resourcesDir = Path.Combine(projectDirectory, "resources");
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
