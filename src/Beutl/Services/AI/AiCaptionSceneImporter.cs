using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl.Services.AI;

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

        int layer = scene.Children
            .Select(item => item.ZIndex)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        Point defaultPosition = new(0, scene.FrameSize.Height * 0.35f);
        var context = new CaptionElementContext(
            layer,
            Beutl.Language.Strings.AiSubtitle,
            defaultPosition);
        var descriptions = new List<ElementDescription>(document.Count);
        foreach (CaptionCue cue in document.Cues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            descriptions.AddRange(template.CreateElements(cue, context));
        }
        ElementAddResult result = await adder.AddAsync(descriptions, cancellationToken);
        return new CaptionSceneImportResult(result.IsSuccess, result.Failure?.Id);
    }
}
