using System.IO.Enumeration;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;

namespace Beutl.Views.Tools;

internal enum AiFileDropTarget
{
    None,
    ReferenceImage,
    SourceImage,
    FirstFrame,
    LastFrame,
    SourceVideo,
    CharacterImage,
    VideoReference,
    Captions,
}

internal static class AiFileDrop
{
    public static readonly AttachedProperty<AiFileDropTarget> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, AiFileDropTarget>("Target", typeof(AiFileDrop));

    static AiFileDrop()
    {
        TargetProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.GetOldValue<AiFileDropTarget>() != AiFileDropTarget.None)
            {
                control.RemoveHandler(DragDrop.DragEnterEvent, OnDragOver);
                control.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
                control.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                control.RemoveHandler(DragDrop.DropEvent, OnDrop);
            }

            bool enabled = change.GetNewValue<AiFileDropTarget>() != AiFileDropTarget.None;
            control.Classes.Set("aifiledrop", enabled);
            control.Classes.Set("dragover", false);
            if (enabled)
            {
                DragDrop.SetAllowDrop(control, true);
                control.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                control.AddHandler(DragDrop.DropEvent, OnDrop);
            }
            else
            {
                control.ClearValue(DragDrop.AllowDropProperty);
            }
        });
    }

    public static AiFileDropTarget GetTarget(Control control) => control.GetValue(TargetProperty);

    public static void SetTarget(Control control, AiFileDropTarget value) => control.SetValue(TargetProperty, value);

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        bool accepted = GetRequest(control) is { } request && GetPaths(e.DataTransfer, request).Count > 0;
        control.Classes.Set("dragover", accepted);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = e.DataTransfer.Contains(DataFormat.File);
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control control) control.Classes.Set("dragover", false);
    }

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control) return;
        control.Classes.Set("dragover", false);
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (GetRequest(control) is not { } request) return;
        IReadOnlyList<string> paths = GetPaths(e.DataTransfer, request);
        if (paths.Count == 0) return;

        e.DragEffects = DragDropEffects.Copy;
        control.Classes.Set("filedropping", true);
        try
        {
            await request.Apply(paths);
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
        finally
        {
            control.Classes.Set("filedropping", false);
        }
    }

    private static DropRequest? GetRequest(Control control)
    {
        if (!control.IsEffectivelyEnabled || !control.IsEffectivelyVisible || control.Classes.Contains("filedropping"))
            return null;

        switch (GetTarget(control))
        {
            case AiFileDropTarget.ReferenceImage when FindContext<AiImageGenerationDialogViewModel>(control) is { } image
                && !image.IsGenerating.Value && image.SupportsReferenceImage.Value && image.CanAddReferenceImage.Value:
                return Request(image.SelectReferenceImage, SharedFilePickerOptions.OpenAiInputImage(), true,
                    paths => image.SelectReferenceImageAsync(paths));
            case AiFileDropTarget.SourceImage when FindContext<AiImageEditDialogViewModel>(control) is { } edit
                && !edit.IsEditing.Value:
                return Request(edit.SelectSourceFileCommand, SharedFilePickerOptions.OpenAiInputImage(), false,
                    paths => edit.SelectSourceFileAsync(paths[0]));
            case AiFileDropTarget.FirstFrame or AiFileDropTarget.LastFrame
                when FindContext<AiVideoGenerationDialogViewModel>(control) is { } video && !video.IsGenerating.Value:
                bool first = GetTarget(control) == AiFileDropTarget.FirstFrame;
                if (!(first ? video.SupportsFirstFrame.Value : video.SupportsLastFrame.Value)) return null;
                if (first && !video.CaptureCurrentFrame.CanExecute()) return null;
                return Request(first ? video.SelectFirstFrame : video.SelectLastFrame,
                    SharedFilePickerOptions.OpenAiVideoFrame(), false, paths => video.SelectFrameAsync(first, paths[0]));
            case AiFileDropTarget.SourceVideo or AiFileDropTarget.CharacterImage
                when FindContext<AiVideoGenerationDialogViewModel>(control) is { } source && !source.IsGenerating.Value:
                string role = GetTarget(control) == AiFileDropTarget.SourceVideo ? "source" : "character";
                if (!(role == "source" ? source.IsSourceVideo : source.IsMotionControl)) return null;
                if (role == "source" && !source.ModelPicker.IsLoaded.Value) return null;
                return Request(role == "source" ? source.SelectSourceVideo : source.SelectCharacterImage,
                    AiVideoGenerationDialogViewModel.GetInputFilePatterns(role), false,
                    paths => source.PickInputAsync(role, paths));
            case AiFileDropTarget.VideoReference when control.DataContext is AiVideoInputGroup group
                && FindContext<AiVideoGenerationDialogViewModel>(control) is { } owner && !owner.IsGenerating.Value
                && owner.ReferenceGroups.Contains(group) && group.IsSupported.Value:
                return Request(group.Pick, AiVideoGenerationDialogViewModel.GetInputFilePatterns(group.Kind), true,
                    paths => owner.PickInputAsync(group.Kind, paths), group.MaximumCount - group.Files.Count,
                    (path, accepted) => owner.CanAddDroppedReference(group, path, accepted));
            case AiFileDropTarget.Captions when FindContext<AiSubtitleDialogViewModel>(control) is { } captions
                && !captions.IsSubtitleOperationActive.Value && ((ICommand)captions.ImportCaptions).CanExecute(null):
                return new DropRequest((path, _) => captions.CanImportCaptionFile(Path.GetFileName(path)), false,
                    paths => captions.ImportCaptionsCore(paths[0]));
            default:
                return null;
        }
    }

    private static DropRequest? Request(ICommand command, FilePickerOpenOptions options, bool multiple,
        Func<IReadOnlyList<string>, Task> apply)
        => Request(command, options.FileTypeFilter!.SelectMany(type => type.Patterns ?? []).ToArray(), multiple, apply);

    private static DropRequest? Request(ICommand command, IReadOnlyList<string> patterns, bool multiple,
        Func<IReadOnlyList<string>, Task> apply, int maximumCount = int.MaxValue,
        Func<string, IReadOnlyList<string>, bool>? filter = null)
        => maximumCount > 0 && command.CanExecute(null)
            ? new DropRequest((path, accepted) => patterns.Any(pattern =>
                FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true))
                && (filter?.Invoke(path, accepted) ?? true), multiple, apply, maximumCount)
            : null;

    private static IReadOnlyList<string> GetPaths(IDataTransfer data, DropRequest request)
    {
        // The drag transfer owns its storage items; local paths remain usable after the event ends.
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IStorageItem item in data.TryGetFiles() ?? [])
        {
            if (item is not IStorageFile || item.TryGetLocalPath() is not { } path
                || !File.Exists(path) || !seen.Add(path) || !request.Accepts(path, paths)) continue;
            paths.Add(path);
            if (!request.Multiple || paths.Count >= request.MaximumCount) break;
        }
        return paths;
    }

    private static T? FindContext<T>(Control control) where T : class
        => control.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(ancestor => ancestor.DataContext).OfType<T>().FirstOrDefault();

    private sealed record DropRequest(Func<string, IReadOnlyList<string>, bool> Accepts, bool Multiple, Func<IReadOnlyList<string>, Task> Apply,
        int MaximumCount = int.MaxValue);
}
