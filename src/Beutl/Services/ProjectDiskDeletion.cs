using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.AgentHost;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

// What deleting a project from disk removes: Path is the project's own folder when IsFolder is set,
// otherwise the project file alone.
internal sealed record ProjectDiskDeletionTarget(string ProjectFile, string Path, bool IsFolder);

// "Delete from Disk" for a project in the start page's recent list. The deletion is permanent, like
// the file browser's. The folder goes with the project only when it is the project's own: the
// <name>/<name>.bep layout the new-project dialog creates, holding no other project and none of the
// folders the user or Beutl keeps. A .bep anywhere else, such as an agent's shared output folder,
// loses only the project file.
internal sealed class ProjectDiskDeletion(ProjectService projectService, EditorService editorService)
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectDiskDeletion>();

    // The entries made writable before the folder is deleted. Links are skipped, so nothing outside
    // the folder is touched.
    private static readonly EnumerationOptions s_folderEntries = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    // The scan for other projects skips links too, since the deletion removes them without following
    // them. A subfolder it cannot list could hold a project, though, so it throws there instead of
    // skipping, and the folder counts as shared.
    private static readonly EnumerationOptions s_projectScan = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
        IgnoreInaccessible = false,
    };

    internal Func<FAContentDialog, Task<FAContentDialogResult>> ConfirmAsync { get; set; } =
        static dialog => dialog.ShowAsync();

    // How the recent files are checked for existence; a test holds it to stand in for a slow share.
    internal Func<string, bool> RecentFileExists { get; set; } = File.Exists;

    public async Task DeleteAsync(string projectFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFile);
        projectFile = Path.GetFullPath(projectFile);
        IReadOnlyList<string> protectedFolders = GetProtectedFolders();
        ProjectDiskDeletionTarget? target = await Task.Run(() => Resolve(projectFile, protectedFolders));
        if (target is null)
        {
            NotificationService.ShowInformation(Strings.DeleteFromDisk, MessageStrings.FileDoesNotExist);
            return;
        }

        if (IsInUse(target)
            || await ConfirmAsync(CreateConfirmation(target)) != FAContentDialogResult.Primary)
        {
            return;
        }

        ProjectDiskDeletionTarget? attempted = null;
        try
        {
            await projectService.RunExclusiveOfTransitionsAsync(async () =>
            {
                // The confirmation can stay open for a while. Delete only what it named, and only
                // while nothing has the project open.
                ProjectDiskDeletionTarget? current = await Task.Run(() => Resolve(projectFile, protectedFolders));
                if (current is null)
                {
                    NotificationService.ShowInformation(Strings.DeleteFromDisk, MessageStrings.FileDoesNotExist);
                    return;
                }

                if (current != target)
                {
                    NotificationService.ShowError(Strings.DeleteFromDisk, MessageStrings.OperationFailed);
                    return;
                }

                if (IsInUse(current))
                {
                    return;
                }

                // An export outlives the close of its project and keeps its output lease, so files no
                // editor holds any more can still be read. Reserving the workspace refuses while an
                // export, a save or a version-control operation runs, and holds new ones off meanwhile.
                using IDisposable? workspace = editorService.TryBeginWorktreeMutation();
                if (workspace is null)
                {
                    NotificationService.ShowError(
                        Strings.DeleteFromDisk,
                        MessageStrings.ProjectBusyCannotDeleteFromDisk);
                    return;
                }

                attempted = current;
                try
                {
                    await Task.Run(() => Delete(current));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    s_logger.LogError(ex, "Failed to delete a project from disk. Path: {Path}", current.Path);
                    // Part of the folder may be left behind; the exception names what could not go.
                    NotificationService.ShowError(Strings.DeleteFromDisk, ex.Message);
                }
            });
        }
        finally
        {
            // Checking the recent files can block on an unrelated one, as on a disconnected network
            // drive, so it waits until the gate and the workspace are free again.
            if (attempted is not null)
            {
                await ForgetDeletedFilesAsync(GlobalConfiguration.Instance.ViewConfig, attempted);
            }
        }
    }

    // Returns null when the project file no longer exists.
    internal static ProjectDiskDeletionTarget? Resolve(
        string projectFile,
        IReadOnlyCollection<string> protectedFolders)
    {
        if (!File.Exists(projectFile))
        {
            return null;
        }

        string? folder = Path.GetDirectoryName(projectFile);
        return folder is not null && IsProjectsOwnFolder(projectFile, folder, protectedFolders)
            ? new ProjectDiskDeletionTarget(projectFile, folder, IsFolder: true)
            : new ProjectDiskDeletionTarget(projectFile, projectFile, IsFolder: false);
    }

    internal static void Delete(ProjectDiskDeletionTarget target)
    {
        // The project file goes first. If it cannot be deleted nothing else is touched, and a failure
        // after it leaves stray files instead of a listed project whose scenes are gone.
        ClearReadOnly(target.ProjectFile);
        File.Delete(target.ProjectFile);
        if (!target.IsFolder)
        {
            return;
        }

        // Git keeps its objects read-only, and Windows refuses to delete read-only entries.
        ClearReadOnly(target.Path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(target.Path, "*", s_folderEntries))
        {
            ClearReadOnly(entry);
        }

        Directory.Delete(target.Path, recursive: true);
    }

    // Folders a project folder must not contain, since deleting it would take them along.
    internal static IReadOnlyList<string> GetProtectedFolders()
    {
        var folders = new List<string>();
        foreach (Environment.SpecialFolder folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            Add(Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify));
        }

        Add(Path.GetTempPath());
        Add(AppContext.BaseDirectory);
        Add(BeutlEnvironment.GetHomeDirectoryPath());
        Add(AgentHostEndpoint.ResolveWorkspaceRoot(GlobalConfiguration.Instance.AiAgentConfig));
        return folders.Distinct().ToArray();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                folders.Add(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A path that cannot be resolved names no folder a project could contain.
            }
        }
    }

    private static bool IsProjectsOwnFolder(
        string projectFile,
        string folder,
        IReadOnlyCollection<string> protectedFolders)
    {
        // The new-project dialog always creates <location>/<name>/<name>.bep in a new folder. A root
        // is never a project's own folder, whatever its name.
        if (Path.GetDirectoryName(folder) is null
            || !string.Equals(
                Path.GetFileName(folder),
                Path.GetFileNameWithoutExtension(projectFile),
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            // A link is deleted as a link. A linked folder would keep the files the confirmation
            // names, and a linked project file would leave its project behind while the rest of
            // the folder went; the scan below skips links, so it would not see that file either.
            if (IsLink(folder)
                || IsLink(projectFile)
                || protectedFolders.Any(path => FilePathComparison.IsSameOrDescendant(folder, path)))
            {
                return false;
            }

            // Another project inside would be deleted along with this one.
            return Directory
                .EnumerateFiles(folder, $"*.{EditorConstants.ProjectFileExtension}", s_projectScan)
                .All(file => FilePathComparison.AreSameCanonicalPath(file, projectFile));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            // A folder that cannot be inspected is treated as shared.
            s_logger.LogWarning(ex, "Could not inspect the project folder {Folder}.", folder);
            return false;
        }
    }

    // Drops the recent entries of what is gone: the project file and, with the folder, its scenes.
    // The lists are read and changed on the UI thread, but checking their files can block, as on a
    // disconnected network drive.
    private async Task ForgetDeletedFilesAsync(ViewConfig viewConfig, ProjectDiskDeletionTarget target)
    {
        // The gate is free during the check, so a project can be opened or created at a deleted path
        // meanwhile; an entry listed again since the check started is newer than its answer.
        var listedAgain = new HashSet<string>(StringComparer.Ordinal);
        void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (string file in e.NewItems?.OfType<string>() ?? [])
            {
                listedAgain.Add(file);
            }
        }

        viewConfig.RecentFiles.CollectionChanged += OnListChanged;
        viewConfig.RecentProjects.CollectionChanged += OnListChanged;
        try
        {
            string[] recent = viewConfig.RecentFiles.Concat(viewConfig.RecentProjects).Distinct().ToArray();
            Func<string, bool> exists = RecentFileExists;
            string[] deleted = await Task.Run(() => recent
                .Where(file => !exists(file) && WasDeleted(target, file))
                .ToArray());
            foreach (string file in deleted.Where(file => !listedAgain.Contains(file)))
            {
                viewConfig.RecentFiles.Remove(file);
                viewConfig.RecentProjects.Remove(file);
            }
        }
        finally
        {
            viewConfig.RecentFiles.CollectionChanged -= OnListChanged;
            viewConfig.RecentProjects.CollectionChanged -= OnListChanged;
        }
    }

    private static bool IsLink(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static void ClearReadOnly(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        // A link is removed, not written through, so its target keeps its attributes.
        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != FileAttributes.ReadOnly)
        {
            return;
        }

        attributes &= ~FileAttributes.ReadOnly;
        File.SetAttributes(path, attributes == 0 ? FileAttributes.Normal : attributes);
    }

    private static bool WasDeleted(ProjectDiskDeletionTarget target, string file)
    {
        try
        {
            return target.IsFolder
                ? FilePathComparison.IsSameOrDescendant(target.Path, file)
                : FilePathComparison.AreSameCanonicalPath(target.Path, file);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    private static FAContentDialog CreateConfirmation(ProjectDiskDeletionTarget target)
    {
        return new FAContentDialog
        {
            Title = Strings.DeleteFromDisk,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = target.IsFolder
                            ? MessageStrings.ConfirmDeleteProjectFolderFromDisk
                            : MessageStrings.ConfirmDeleteProjectFileFromDisk,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new SelectableTextBlock { Text = target.Path, TextWrapping = TextWrapping.Wrap },
                },
            },
            PrimaryButtonText = Strings.Delete,
            CloseButtonText = Strings.Cancel,
            // Enter must not delete.
            DefaultButton = FAContentDialogButton.Close,
        };
    }

    private bool IsInUse(ProjectDiskDeletionTarget target)
    {
        try
        {
            // Only what this deletion removes counts: every file under the folder, or the project
            // file alone, whose scenes stay on disk and may stay open.
            if (!editorService.IsFileInUse(target.Path, target.IsFolder))
            {
                return false;
            }

            NotificationService.ShowError(Strings.DeleteFromDisk, MessageStrings.ProjectInUseCannotDeleteFromDisk);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not verify whether {Path} is in use.", target.Path);
            NotificationService.ShowError(Strings.DeleteFromDisk, MessageStrings.OperationFailed);
        }

        return true;
    }
}
