using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Utilities;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public partial class EditorService
{
    internal void OpenUndisplayableData(Project project)
    {
        Scene? active = (SelectedTabItem.Value?.Context.Value as EditViewModel)?.Scene;
        Scene[] scenes = project.Items.OfType<Scene>().Where(scene => scene.Uri is not null).ToArray();
        if (active is not null && !scenes.Contains(active)) active = null;

        Scene? target = active ?? scenes.FirstOrDefault();
        try
        {
            target = UndisplayableDataScanner.Scan(project, active).FirstOrDefault(
                entry => entry.Scene is { Uri: not null })?.Scene ?? target;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            _logger.LogWarning(ex, "Could not find the scene with undisplayable data in project {ProjectId}", project.Id.Value);
        }

        if (target is null) return;
        ActivateTabItem(target);
        if (TryGetTabItem(target, out var tab) && tab.Context.Value is EditViewModel editor)
            editor.OpenUndisplayableData();
    }
}
