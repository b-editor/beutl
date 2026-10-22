using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

// Keyframe clipboard steps shared by the graph editor and the timeline's inline animation layers.
// Callers pass their own logger so the log category stays that of the calling view model.
internal static class KeyFrameClipboardCommands
{
    internal static Task CopyAnimationAsync(ICoreSerializable animation, ILogger logger)
    {
        return CopyAsync(animation, BeutlDataFormats.KeyFrameAnimation, ex =>
        {
            logger.LogError(ex, "Failed to copy all keyframes");
            NotificationService.ShowError(Strings.Copy, MessageStrings.FailedToCopyAnimation);
        });
    }

    // Pastes the first of a keyframe, a keyframe selection and an animation that the clipboard holds.
    // Reading the clipboard yields, so canPaste is asked again before anything is pasted.
    internal static async Task PasteAtPositionAsync(
        IClipboard clipboard, Func<bool> canPaste, Func<bool> canShowFailure, Action<string> pasteKeyFrame,
        Action<string> pasteSelection, Action<string> pasteAnimation, ILogger logger)
    {
        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } keyFrameJson)
            {
                if (canPaste()) pasteKeyFrame(keyFrameJson);
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameSelection) is { } selectionJson)
            {
                if (canPaste()) pasteSelection(selectionJson);
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation) is { } keyFrameAnimationJson)
            {
                if (canPaste()) pasteAnimation(keyFrameAnimationJson);
                return;
            }

            if (canPaste()) NotificationService.ShowWarning(Strings.Paste, MessageStrings.InvalidKeyframeDataFormat);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to paste keyframe at position");
            if (canShowFailure()) NotificationService.ShowError(Strings.Paste, MessageStrings.FailedToPasteKeyframe);
        }
    }

    internal static void PasteAnimation(IServiceProvider services, IKeyFrameAnimation animation, string json, ILogger logger)
    {
        logger.LogInformation("Pasting JSON");
        var keyFrameAnimation = (IKeyFrameAnimation)animation;
        IKeyFrameClipboardService service = services.GetRequiredService<IKeyFrameClipboardService>();
        KeyFrameAnimationPasteOutcome outcome = service.PasteAnimation(keyFrameAnimation, json);

        ReportAnimationPaste(outcome, keyFrameAnimation, logger);
    }

    internal static void PasteKeyFrame(
        IServiceProvider services, IKeyFrameAnimation animation, string json, Func<IKeyFrameAnimation, MediaTime> getKeyTime,
        Action<Easing> insertWithEasing, ILogger logger)
    {
        logger.LogInformation("Pasting JSON");
        var keyFrameAnimation = (IKeyFrameAnimation)animation;
        MediaTime keyTime = getKeyTime(keyFrameAnimation);
        IKeyFrameClipboardService service = services.GetRequiredService<IKeyFrameClipboardService>();
        KeyFramePasteResult result = service.PasteKeyFrame(keyFrameAnimation, json, keyTime);

        ReportKeyFramePaste(result, logger, insertWithEasing);
    }

    // The easing replaces that of the keyframe near keyTime, the drop position as a key time, or comes with a
    // new keyframe inserted at dropTime.
    internal static void DropEasing(
        IKeyFrameAnimation animation, Scene scene, HistoryManager history, Easing easing, MediaTime dropTime,
        MediaTime keyTime, Action<Easing, MediaTime> insertKeyFrame, ILogger? logger)
    {
        IKeyFrame? keyFrame = FindEasingDropTarget(animation, keyTime, scene);
        if (keyFrame != null)
        {
            logger?.LogInformation("Editing existing key frame at {KeyTime}", keyTime);
            keyFrame.Easing = easing;
            history.Commit(CommandNames.ChangeEasing);
        }
        else
        {
            insertKeyFrame(easing, dropTime);
        }
    }

    // The easing that an easing dragged from the library creates.
    internal static bool TryCreateDroppedEasing(IDataTransfer data, [NotNullWhen(true)] out Easing? easing)
    {
        easing = data.TryGetValue(BeutlDataFormats.Easing) is { } typeName && TypeFormat.ToType(typeName) is { } type
            ? Activator.CreateInstance(type) as Easing
            : null;
        return easing != null;
    }

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

    internal static void ReportAnimationPaste(KeyFrameAnimationPasteOutcome outcome, IKeyFrameAnimation animation, ILogger logger)
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
    internal static IKeyFrame? FindEasingDropTarget(IKeyFrameAnimation animation, MediaTime keyTime, Scene scene)
    {
        MediaTime threshold = MediaTime.FromFrames(3, SceneTimeRangeService.GetFrameRate(scene));

        return animation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Flicks - keyTime.Flicks) <= threshold.Flicks);
    }
}
