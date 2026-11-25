using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

internal static class SceneFileDrop
{
    private static readonly ILogger s_logger = Log.CreateLogger(nameof(SceneFileDrop));

    public static string[] GetLocalPaths(IEnumerable<IStorageItem>? items)
        => items?.OfType<IStorageFile>().Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];

    public static async Task AddAsync(
        IEditorContext editor,
        Scene scene,
        IReadOnlyList<string> files,
        MediaTime start,
        int layer,
        Control target,
        CaptionTemplateChooser captionChooser,
        TimelineTabViewModel? timeline,
        Point? position = null,
        StorageDragData? storage = null)
    {
        var notifications = editor.GetRequiredService<INotificationService>();
        try
        {
            using var fileWrite = storage is null ? null
                : HostProjectFileWriteAdmission.Resolve(editor)?.TryBeginProjectFileWrite();
            if (storage is not null && fileWrite is null)
            {
                notifications.ShowWarning(Strings.CloudStorage, Strings.FileBrowser_WorkspaceBusy);
                return;
            }

            using var import = storage is null ? null : await storage.ImportToSceneAsync(scene);
            var importer = editor.GetRequiredService<SceneFileImporter>();
            SceneFileImportResult? result = await importer.AddAsync(
                scene, import?.Paths ?? files, start, layer, position,
                templates => captionChooser.ChooseAsync(target, templates));
            if (result is null) return;

            if (!result.AddResult.IsSuccess)
            {
                if (result.AddResult.Failure is LockedElementLayerFailure)
                {
                    notifications.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
                }
                else
                {
                    s_logger.LogError(result.AddResult.Failure?.Exception, "Failed to add dropped files: {FailureId}",
                        result.AddResult.Failure?.Id);
                    notifications.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
                }
                return;
            }

            if (import is not null)
            {
                foreach (ElementAddItemResult item in result.AddResult.Items.Take(result.FirstCaptionIndex))
                {
                    if (item.Description.Source is ElementSource.File file)
                    {
                        import.Retain(file.FileName);
                    }
                    else
                    {
                        // Templates can reference other resources from the same drop.
                        import.RetainAll();
                        break;
                    }
                }
            }

            Element scrollTarget = result.FirstCaption ?? result.AddResult.Items[0].PrimaryElement;
            timeline?.ScrollTo.Execute((scrollTarget.Range, scrollTarget.ZIndex.Value));
            importer.ReportSkippedCues(result.FilesWithSkippedCues);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Could not import dropped files into the scene.");
            if (storage is null)
                notifications.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
            else
                notifications.ShowError(Strings.CloudStorage, Strings.CloudStorageActionFailed);
        }
    }
}
