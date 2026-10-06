using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

// Keyframe clipboard steps shared by the graph editor and the timeline's inline animation layers.
// Callers pass their own logger so the log category stays that of the calling view model.
internal static class KeyFrameClipboardCommands
{
    internal static async Task CopyAsync(ICoreSerializable source, DataFormat<string> format, Action<Exception> onFailure)
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return;

        try
        {
            ObjectRegenerator.Regenerate(source, out string json);

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(format, json));

            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            onFailure(ex);
        }
    }

    internal static void ReportAnimationPaste(KeyFrameAnimationPasteOutcome outcome, KeyFrameAnimation animation, ILogger logger)
    {
        switch (outcome)
        {
            case KeyFrameAnimationPasteOutcome.Pasted:
                break;
            case KeyFrameAnimationPasteOutcome.InvalidJson:
                logger.LogError("Invalid JSON");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJson);
                break;
            case KeyFrameAnimationPasteOutcome.MissingType:
                logger.LogError("Invalid JSON: missing $type");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_MissingType);
                break;
            case KeyFrameAnimationPasteOutcome.TypeIsNotKeyFrameAnimation:
                logger.LogError("Invalid JSON: $type is not a KeyFrameAnimation");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_TypeIsNotKeyFrameAnimation);
                break;
            case KeyFrameAnimationPasteOutcome.GenericTypeMismatch:
                logger.LogError("The property type of the pasted animation does not match.");
                NotificationService.ShowError(
                    Strings.GraphEditor,
                    string.Format(MessageStrings.AnimationPropertyTypeMismatch, animation.ValueType.Name, "?"));
                break;
            case KeyFrameAnimationPasteOutcome.UnexpectedError:
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.FailedToPasteKeyframe);
                break;
        }
    }

    internal static void ReportKeyFramePaste(KeyFramePasteResult result, ILogger logger, Action<Easing> insertWithEasing)
    {
        switch (result.Outcome)
        {
            case KeyFramePasteOutcome.Inserted:
                break;
            case KeyFramePasteOutcome.ReplacedExisting:
                NotificationService.ShowWarning(Strings.GraphEditor, MessageStrings.KeyframeExistsAtPastePosition);
                break;
            case KeyFramePasteOutcome.GenericTypeMismatch when result.EasingForFallback is { } easing:
                // Type mismatch: insert a fresh keyframe via the View's typed path,
                // carrying over only the clipboard's easing.
                insertWithEasing(easing);
                NotificationService.ShowWarning(Strings.GraphEditor, MessageStrings.KeyframePropertyTypeMismatch_EasingApplied);
                break;
            case KeyFramePasteOutcome.InvalidJson:
                logger.LogError("Invalid JSON");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJson);
                break;
            case KeyFramePasteOutcome.MissingType:
                logger.LogError("Invalid JSON: missing $type");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_MissingType);
                break;
            case KeyFramePasteOutcome.TypeIsNotKeyFrame:
                logger.LogError("Invalid JSON: $type is not a KeyFrame");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_TypeIsNotKeyFrame);
                break;
            case KeyFramePasteOutcome.UnexpectedError:
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.FailedToPasteKeyframe);
                break;
        }
    }

    // A dropped easing lands on an existing keyframe within three frames of the drop position.
    internal static IKeyFrame? FindEasingDropTarget(IKeyFrameAnimation animation, TimeSpan keyTime, Scene scene)
    {
        Project? proj = scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan threshold = TimeSpan.FromSeconds(1d / rate) * 3;

        return animation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Ticks - keyTime.Ticks) <= threshold.Ticks);
    }
}
