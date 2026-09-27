using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public abstract partial class GraphEditorViewModel
{
    internal async Task<bool> CopySelectionAsync(IClipboard? clipboard = null)
    {
        clipboard ??= ClipboardHelper.GetClipboard();
        if (SelectedView.Value is not { } channel || clipboard == null) return false;
        var selected = channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray();
        if (selected.Length == 0) return false;
        try
        {
            var copy = (KeyFrameAnimation)Activator.CreateInstance(Animation.GetType())!;
            foreach (var key in selected)
            {
                ObjectRegenerator.Regenerate(key, key.GetType(), out var clone);
                copy.KeyFrames.Add((IKeyFrame)clone);
            }
            ObjectRegenerator.Regenerate(copy, out string json);
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(BeutlDataFormats.KeyFrameAnimation, json));
            await clipboard.SetDataAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy selected keyframes");
            NotificationService.ShowError(Strings.Copy, MessageStrings.FailedToCopyKeyframe);
            return false;
        }
    }

    internal async Task PasteSelectionAsync(IClipboard? clipboard = null)
    {
        clipboard ??= ClipboardHelper.GetClipboard();
        if (clipboard == null || SelectedView.Value is not { } channel) return;
        try
        {
            string? json = await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation)
                ?? await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame);
            if (_disposed || json == null || JsonNode.Parse(json) is not JsonObject data
                || !data.TryGetDiscriminator(out Type? type)) return;
            if (type != Animation.GetType() && type != typeof(KeyFrame<>).MakeGenericType(Animation.ValueType))
            {
                NotificationService.ShowWarning(Strings.Paste, MessageStrings.InvalidKeyframeDataFormat);
                return;
            }
            var source = (ICoreSerializable)Activator.CreateInstance(type)!;
            CoreSerializer.PopulateFromJsonObject(source, data);
            IKeyFrame[] copied = source is KeyFrameAnimation animation ? animation.KeyFrames.ToArray() : [(IKeyFrame)source];
            if (copied.Length == 0) return;
            double first = copied.Min(x => x.KeyTime.TotalSeconds);
            TimeSpan start = ConvertKeyTime(CurrentTime.Value);
            if (start < TimeSpan.Zero) start = TimeSpan.Zero;
            var pasted = new List<IKeyFrame>();
            HistoryManager.ExecuteInTransaction(() =>
            {
                foreach (var key in copied)
                {
                    ObjectRegenerator.Regenerate(key, key.GetType(), out var cloned);
                    var clone = (IKeyFrame)cloned;
                    clone.KeyTime = start + TimeSpan.FromSeconds(key.KeyTime.TotalSeconds - first);
                    var existing = Animation.KeyFrames.FirstOrDefault(x => x.KeyTime == clone.KeyTime);
                    if (existing != null)
                    {
                        existing.Value = clone.Value;
                        existing.Easing = clone.Easing;
                        pasted.Add(existing);
                    }
                    else
                    {
                        Animation.KeyFrames.Add(clone, out _);
                        pasted.Add(clone);
                    }
                }
            }, CommandNames.PasteKeyFrame);
            channel.SetSelection(pasted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste selected keyframes");
            NotificationService.ShowError(Strings.Paste, MessageStrings.FailedToPasteKeyframe);
        }
    }
}
