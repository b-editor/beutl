using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed partial class EditorService
{
    // The tabs of a session put aside while another one is active, kept in the workspace of its project. The tabs
    // stay whole, so the session's edits, selection, undo history and unsaved sidecars come back with it. Selected is
    // the tab that was selected; Project is the model they edit, so a later model of the same file never takes them.
    internal sealed record InactiveTabSet(Project Project, EditorTabItem[] Items, EditorTabItem? Selected);

    // Every tab put aside, for checks that cover every editor this service owns.
    internal IEnumerable<EditorTabItem> InactiveTabItems
        => Workspaces.SelectMany(workspace => workspace.InactiveTabs?.Items ?? []);

    internal bool HasInactiveTabs(Project project) => TryGetInactiveTabs(project, out _);

    // Takes the open tabs out of the tab list and keeps them in the workspace of project, which the editor stops
    // showing. Their playback stops; nothing is saved or disposed.
    internal async Task PutTabsAsideAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Dispatcher.UIThread.VerifyAccess();
        EditorWorkspace workspace = WorkspaceOfFile(project);
        // Captured, cleared and kept with no await in between, so a tab opened or closed meanwhile can neither be
        // dropped from the list unkept nor be kept after it was disposed.
        EditorTabItem[] items = [.. _tabItems];
        EditorTabItem? selected = SelectedTabItem.Value is { } tab && items.Contains(tab) ? tab : null;
        SelectedTabItem.Value = null;
        _tabItems.Clear();
        if (workspace.InactiveTabs is { } earlier && ReferenceEquals(earlier.Project, project))
        {
            // A session cannot be put aside twice; keep both sets rather than lose the earlier tabs.
            items = [.. earlier.Items, .. items];
            selected ??= earlier.Selected;
        }
        else if (workspace.InactiveTabs is { } stale)
        {
            // Another model of the same file left tabs behind; they are not this project's to take over.
            _logger.LogWarning("Replaced {Count} tabs put aside for another model of {File}.", stale.Items.Length, workspace.ProjectFile);
        }

        workspace.InactiveTabs = new InactiveTabSet(project, items, selected);

        foreach (EditViewModel editor in items.Select(item => item.Context.Value).OfType<EditViewModel>())
        {
            if (editor.IsDisposingOrDisposed)
                continue;

            try
            {
                await editor.Player.Pause();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pause an editor that is put aside.");
            }
        }
    }

    // Returns the tabs put aside for project and leaves them there.
    internal bool TryGetInactiveTabs(Project project, [NotNullWhen(true)] out InactiveTabSet? tabs)
    {
        ArgumentNullException.ThrowIfNull(project);
        tabs = WorkspaceOfFile(project).InactiveTabs is { } set && ReferenceEquals(set.Project, project) ? set : null;
        return tabs is not null;
    }

    // Removes and returns the tabs put aside for project.
    internal bool TryTakeInactiveTabs(Project project, [NotNullWhen(true)] out InactiveTabSet? tabs)
    {
        if (!TryGetInactiveTabs(project, out tabs))
            return false;

        WorkspaceOfFile(project).InactiveTabs = null;
        ScheduleWorkspaceRelease();
        return true;
    }

    // Removes and returns every tab put aside, for shutdown.
    internal EditorTabItem[] TakeAllInactiveTabs()
    {
        var items = new List<EditorTabItem>();
        foreach (EditorWorkspace workspace in Workspaces)
        {
            if (workspace.InactiveTabs is { } set)
            {
                workspace.InactiveTabs = null;
                items.AddRange(set.Items);
            }
        }

        ScheduleWorkspaceRelease();

        return [.. items];
    }
}
