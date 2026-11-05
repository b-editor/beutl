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
            int shift = 0;
            while (elements.Any(element => layerEnds.TryGetValue(element.Layer + shift, out MediaTime end) && element.Start < end))
                shift++;

            if (shift > 0)
            {
                elements = template.CreateElements(cue, new CaptionElementContext(
                    firstLayer + shift, context.ElementName, context.DefaultPosition, context.MinimumLength));
            }

            foreach (ElementDescription element in elements)
            {
                MediaTime end = element.Start + (element.Length ?? MediaTime.Zero);
                if (!layerEnds.TryGetValue(element.Layer, out MediaTime previous) || previous < end)
                    layerEnds[element.Layer] = end;
                descriptions.Add(element with { StacksAboveScene = true });
            }
        }

        return descriptions;
    }
}
