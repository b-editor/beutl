using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal readonly record struct CaptionSceneImportResult(
    bool IsSuccess,
    ElementAddFailureId? FailureId);

internal static class AiCaptionSceneImporter
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

    // Where a request lands: one layer, or for several elements kept together, the layers they span from its layer up.
    // Those are placed by their whole time range, and another set goes above them all rather than between them.
    internal static IEnumerable<(int Layer, MediaTime Start, MediaTime End)> GetExtents(ElementDescription description)
    {
        if (description.Source is not ElementSource.ElementSetTemplate source)
            return [(description.Layer, description.Start, description.Start + (description.Length ?? MediaTime.Zero))];

        IList<Element> elements = source.Factory().Elements;
        MediaTime length = elements.Max(element => element.Range.End) - elements.Min(element => element.Start.Value);
        int span = elements.Max(element => element.ZIndex.Value) - elements.Min(element => element.ZIndex.Value) + 1;
        return Enumerable.Range(description.Layer, span)
            .Select(layer => (layer, description.Start, description.Start + length));
    }
}
