using System.Text.Json.Nodes;
using Beutl.Animation;

namespace Beutl.AgentToolkit.Reconciliation;

internal static class KeyFrameValueChangeDetector
{
    public static HashSet<Guid>? CollectChangedValueIds(
        JsonObject? currentAnimation,
        JsonObject desiredAnimation)
    {
        if (desiredAnimation[nameof(KeyFrameAnimation.KeyFrames)] is not JsonArray desiredKeyFrames)
        {
            return [];
        }

        if (currentAnimation?[nameof(KeyFrameAnimation.KeyFrames)] is not JsonArray currentKeyFrames)
        {
            return null;
        }

        var currentById = new Dictionary<Guid, JsonObject>();
        foreach (JsonObject currentKeyFrame in currentKeyFrames.OfType<JsonObject>())
        {
            if (CollectionReconciler.TryGetId(currentKeyFrame, out Guid id))
            {
                currentById[id] = currentKeyFrame;
            }
        }

        var changed = new HashSet<Guid>();
        foreach (JsonObject desiredKeyFrame in desiredKeyFrames.OfType<JsonObject>())
        {
            if (!CollectionReconciler.TryGetId(desiredKeyFrame, out Guid id))
            {
                return null;
            }

            if (!currentById.TryGetValue(id, out JsonObject? currentKeyFrame)
                || ValueChanged(currentKeyFrame, desiredKeyFrame))
            {
                changed.Add(id);
            }
        }

        return changed;
    }

    private static bool ValueChanged(JsonObject currentKeyFrame, JsonObject desiredKeyFrame)
    {
        bool currentHasValue = currentKeyFrame.TryGetPropertyValue(
            nameof(IKeyFrame.Value),
            out JsonNode? currentValue);
        bool desiredHasValue = desiredKeyFrame.TryGetPropertyValue(
            nameof(IKeyFrame.Value),
            out JsonNode? desiredValue);
        return currentHasValue != desiredHasValue
               || !JsonNode.DeepEquals(currentValue, desiredValue);
    }
}
