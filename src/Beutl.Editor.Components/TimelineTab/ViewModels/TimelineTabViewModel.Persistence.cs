using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading TimelineViewModel state from JSON.");

        if (json.TryGetPropertyValue(nameof(LayerHeaders), out JsonNode? layersNode)
            && layersNode is JsonArray layersArray)
        {
            foreach ((LayerHeaderViewModel layer, JsonObject item) in layersArray.OfType<JsonObject>()
                         .Select(v =>
                             v.TryGetPropertyValueAsJsonValue(nameof(LayerHeaderViewModel.Number), out int number)
                                 ? (number, v)
                                 : (-1, null))
                         .Where(v => v.Item2 != null)
                         .Join(
                             LayerHeaders,
                             x => x.Item1,
                             y => y.Number.Value,
                             (x, y) => (y, x.Item2!)))
            {
                layer.ReadFromJson(item);
                _logger.LogDebug("LayerHeader {Number} state restored from JSON.", layer.Number.Value);
            }
        }

        if (json.TryGetPropertyValue(nameof(Inlines), out JsonNode? inlinesNode)
            && inlinesNode is JsonArray inlinesArray)
        {
            RestoreInlineAnimation(inlinesArray);
        }

        if (json.TryGetPropertyValue(nameof(ThumbnailsDisabledElements), out JsonNode? ThumbnailsDisabledNode)
            && ThumbnailsDisabledNode is JsonArray thumbnailsDisabledArray)
        {
            ThumbnailsDisabledElements.Clear();
            foreach (JsonNode? item in thumbnailsDisabledArray)
            {
                if (item is JsonValue value
                    && value.TryGetValue(out string? guidStr)
                    && Guid.TryParse(guidStr, out Guid id)
                    && Scene.Children.Any(e => e.Id == id))
                {
                    ThumbnailsDisabledElements.Add(id);
                }
            }
        }

        _logger.LogInformation("TimelineViewModel state read from JSON successfully.");
    }

    private void RestoreInlineAnimation(JsonArray inlinesArray)
    {
        _logger.LogInformation("Restoring inline animations from JSON.");

        static (Guid ElementId, Guid AnimationId) GetIds(JsonObject v)
        {
            return v.TryGetPropertyValueAsJsonValue("ElementId", out Guid elementId)
                   && v.TryGetPropertyValueAsJsonValue("AnimationId", out Guid anmId)
                ? (elementId, anmId)
                : (Guid.Empty, Guid.Empty);
        }

        foreach ((Element element, Guid anmId) in inlinesArray.OfType<JsonObject>()
                     .Select(GetIds)
                     .Where(x => x.AnimationId != Guid.Empty && x.ElementId != Guid.Empty)
                     .Join(Scene.Children,
                         x => x.ElementId,
                         y => y.Id,
                         (x, y) => (y, x.AnimationId)))
        {
            IAnimatablePropertyAdapter? anmProp = null;
            EngineObject? engineObject = null;

            void FindAndSetAncestor(Span<object> span, KeyFrameAnimation kfAnm)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    switch (span[i])
                    {
                        case IAnimatablePropertyAdapter anmProp2 when ReferenceEquals(anmProp2.Animation, kfAnm):
                            anmProp = anmProp2;
                            return;
                        case EngineObject engineObject2:
                            engineObject = engineObject2;
                            return;
                    }
                }
            }

            bool Predicate(Stack<object> stack, object obj)
            {
                if (obj is IProperty { Animation: KeyFrameAnimation kfAnm } && kfAnm.Id == anmId)
                {
                    using var pooledArray = new PooledArray<object>(stack.Count);
                    // 同じものが見つかった時に、上の階層から、IAbstractPropertyやAnimatableを探す。
                    stack.CopyTo(pooledArray._array, 0);
                    FindAndSetAncestor(pooledArray.Span, kfAnm);
                    return true;
                }

                return false;
            }

            // Matching any IProperty that carries the animation, rather than only top-level animatable
            // properties, also finds animations nested in other objects such as a Pen.
            var searcher = new ObjectSearcher(element, Predicate);

            if (searcher.Search() is IProperty { Animation: KeyFrameAnimation anm } prop)
            {
                if (anmProp != null)
                {
                    AttachInline(anmProp, element);
                    _logger.LogDebug("Inline animation attached for element {ElementId} and animation {AnimationId}.",
                        element.Id, anmId);
                }
                else if (engineObject != null)
                {
                    try
                    {
                        Type type = typeof(AnimatablePropertyAdapter<>).MakeGenericType(anm.ValueType);
                        var createdProp =
                            (IAnimatablePropertyAdapter)Activator.CreateInstance(type, prop, engineObject)!;
                        AttachInline(createdProp, element);
                        _logger.LogDebug(
                            "Inline animation created and attached for element {ElementId} and animation {AnimationId}.",
                            element.Id, anmId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "An exception occurred while restoring the inline animation for element {ElementId} and animation {AnimationId}.",
                            element.Id, anmId);
                    }
                }
            }
        }

        _logger.LogInformation("Inline animations restored from JSON successfully.");
    }

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing TimelineViewModel state to JSON.");

        var inlines = new JsonArray();
        foreach (InlineAnimationLayerViewModel item in Inlines.OrderBy(v => v.Index.Value))
        {
            if (item.Property.Animation is KeyFrameAnimation { Id: Guid anmId })
            {
                Guid elementId = item.Element.Model.Id;

                inlines.Add(new JsonObject { ["AnimationId"] = anmId, ["ElementId"] = elementId });
                _logger.LogDebug(
                    "Inline animation state written to JSON for element {ElementId} and animation {AnimationId}.",
                    elementId, anmId);
            }
        }

        json[nameof(Inlines)] = inlines;

        var thumbnailsDisabledArray = new JsonArray();
        foreach (Guid id in ThumbnailsDisabledElements)
        {
            thumbnailsDisabledArray.Add(id.ToString());
        }

        json[nameof(ThumbnailsDisabledElements)] = thumbnailsDisabledArray;

        _logger.LogInformation("TimelineViewModel state written to JSON successfully.");
    }
}
