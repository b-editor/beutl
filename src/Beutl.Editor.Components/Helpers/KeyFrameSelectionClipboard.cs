using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Serialization;

namespace Beutl.Editor.Components.Helpers;

internal static class KeyFrameSelectionClipboard
{
    public static (Type Type, IKeyFrame[] Keys)? Read(string? json)
    {
        if (json == null || JsonNode.Parse(json) is not JsonObject data
            || !data.TryGetDiscriminator(out Type? type)
            || !type.IsAssignableTo(typeof(KeyFrame)) && !type.IsAssignableTo(typeof(KeyFrameAnimation))) return null;
        var source = (ICoreSerializable)Activator.CreateInstance(type)!;
        CoreSerializer.PopulateFromJsonObject(source, data);
        IKeyFrame[] copied = source is KeyFrameAnimation animation ? animation.KeyFrames.ToArray() : [(IKeyFrame)source];
        return (type, copied);
    }

    public static IKeyFrame[] Paste(IKeyFrameAnimation animation, IKeyFrame[] copied, TimeSpan start, HistoryManager history)
    {
        if (copied.Length == 0) return [];
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;
        double first = copied.Min(x => x.KeyTime.TotalSeconds);
        var pasted = new List<IKeyFrame>();
        history.ExecuteInTransaction(() =>
        {
            foreach (var key in copied)
            {
                ObjectRegenerator.Regenerate(key, key.GetType(), out var cloned);
                var clone = (IKeyFrame)cloned;
                clone.KeyTime = start + TimeSpan.FromSeconds(key.KeyTime.TotalSeconds - first);
                var existing = animation.KeyFrames.FirstOrDefault(x => x.KeyTime == clone.KeyTime);
                if (existing != null)
                {
                    existing.Value = clone.Value;
                    existing.Easing = clone.Easing;
                    pasted.Add(existing);
                }
                else
                {
                    animation.KeyFrames.Add(clone, out _);
                    pasted.Add(clone);
                }
            }
        }, CommandNames.PasteKeyFrame);
        return pasted.ToArray();
    }
}
