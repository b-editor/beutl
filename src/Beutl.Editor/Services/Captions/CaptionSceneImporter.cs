using System.Collections.Immutable;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.Captions;

internal readonly record struct CaptionSceneImportResult(
    bool IsSuccess,
    ElementAddFailureId? FailureId);

internal static class CaptionSceneImporter
{
    public static async Task<CaptionSceneImportResult> AddAsync(
        Scene scene,
        IElementAdder adder,
        CaptionDocument document,
        CaptionTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(adder);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(template);

        List<ElementDescription> descriptions = CreateDescriptions(scene, 0, document, template, cancellationToken);
        ElementAddResult result = await adder.AddAsync(descriptions, cancellationToken);
        return new CaptionSceneImportResult(result.IsSuccess, result.Failure?.Id);
    }

    // Cues that overlap in time go on separate layers from firstLayer up, counted above the scene as it is when they
    // are added: on one layer, adding them would move the later cue to after the earlier one.
    public static List<ElementDescription> CreateDescriptions(
        Scene scene,
        int firstLayer,
        CaptionDocument document,
        CaptionTemplate template,
        CancellationToken cancellationToken = default,
        string? elementName = null,
        MediaTime? minimumLength = null)
    {
        Point defaultPosition = new(0, scene.FrameSize.Value.Height * 0.35f);
        var context = new CaptionElementContext(
            firstLayer,
            elementName ?? Beutl.Language.Strings.AiSubtitle,
            defaultPosition,
            minimumLength);
        var descriptions = new List<ElementDescription>(document.Count);
        var layerEnds = new Dictionary<int, MediaTime>();
        foreach (CaptionCue cue in document.Cues.OrderBy(cue => cue.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ElementDescription> elements = template.CreateElements(cue, context);
            (int Layer, MediaTime Start, MediaTime End)[] extents = [.. elements.SelectMany(GetExtents)];
            int shift = 0;
            while (extents.Any(extent => layerEnds.TryGetValue(extent.Layer + shift, out MediaTime end) && extent.Start < end))
                shift++;

            if (shift > 0)
            {
                elements = template.CreateElements(cue, new CaptionElementContext(
                    firstLayer + shift, context.ElementName, context.DefaultPosition, context.MinimumLength));
                extents = [.. elements.SelectMany(GetExtents)];
            }

            foreach ((int layer, _, MediaTime end) in extents)
            {
                if (!layerEnds.TryGetValue(layer, out MediaTime previous) || previous < end)
                    layerEnds[layer] = end;
            }

            descriptions.AddRange(elements.Select(element => element with { StacksAboveScene = true }));
        }

        return descriptions;
    }

    // A file session has no UI element adder. Use the editor's materializers and placement
    // inside the caller's history transaction.
    internal static List<Element> AddToFileScene(
        Scene scene, IReadOnlyList<ElementDescription> descriptions, bool autoAdjustDuration, CancellationToken cancellationToken)
    {
        int firstFreeLayer = scene.GetFirstFreeLayer();
        var added = new List<Element>();
        var groups = new List<ImmutableHashSet<Guid>>();
        foreach (ElementDescription description in descriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Element[] elements;
            bool keepsArrangement = description.Source is ElementSource.ElementSetTemplate;
            switch (description.Source)
            {
                case ElementSource.EngineObject source:
                    var value = source.Factory();
                    elements = [ElementDescriptionMaterializer.CreateElementFor(description, value, description.ResolveName(value.GetType()))];
                    break;
                case ElementSource.ElementTemplate source:
                    Element element = source.Factory();
                    ElementDescriptionMaterializer.ApplyTemplatePlacement(element, description);
                    elements = [element];
                    break;
                case ElementSource.ElementSetTemplate source:
                    ElementSet set = source.Factory();
                    elements = [.. set.Elements];
                    GroupPlacement placement = ElementPlacementHelper.PlaceAt(elements, description.Start, description.Layer);
                    ElementPlacementHelper.MoveTogether(elements, placement.StartDelta, placement.LayerDelta);
                    groups.AddRange(set.Groups);
                    break;
                default:
                    throw new NotSupportedException("Unsupported subtitle element source.");
            }

            ElementPlacementHelper.StackAboveScene(scene, firstFreeLayer, elements, keepsArrangement);
            if (keepsArrangement)
                ElementPlacementHelper.PlaceTogether(scene, elements);

            foreach (Element element in elements)
            {
                if (scene.Uri is { } uri)
                    element.Uri = ElementFileNaming.GetUri(uri, element.Id.Value);
                scene.AddChild(element, keepsArrangement ? ElementOverlapHandling.Allow : ElementOverlapHandling.Auto, autoAdjustDuration);
                added.Add(element);
            }
        }

        scene.Groups.AddRange(groups);
        return added;
    }

    // Where a request lands: one layer, or for several elements kept together, the layers they span from its layer up.
    // Those are placed by their whole time range, and another set goes above them all rather than between them.
    internal static IEnumerable<(int Layer, MediaTime Start, MediaTime End)> GetExtents(ElementDescription description)
    {
        if (description.Source is not ElementSource.ElementSetTemplate source)
            return [(description.Layer, description.Start, description.Start + (description.Length ?? MediaTime.Zero))];

        (TimeRange range, int minZIndex, int maxZIndex) = DuplicateHelper.ComputePlacementRange(source.Factory().Elements);
        return Enumerable.Range(description.Layer, maxZIndex - minZIndex + 1)
            .Select(layer => (layer, description.Start, description.Start + range.Duration));
    }
}
