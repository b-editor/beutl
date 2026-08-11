using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Services.AI;

internal readonly record struct CaptionSceneImportResult(
    bool IsSuccess,
    ElementAddFailureId? FailureId);

internal static class AiCaptionSceneImporter
{
    public static async Task<CaptionSceneImportResult> AddAsync(
        EditViewModel editViewModel,
        CaptionDocument document,
        CaptionTemplateRegistry templates,
        CaptionTemplateId templateId,
        GenerationProvenance? provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editViewModel);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(templates);

        using CaptionTemplateLease template = templates.Acquire(templateId);
        Scene scene = editViewModel.Scene;
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
        if (provenance is not null)
        {
            for (int i = 0; i < descriptions.Count; i++)
            {
                descriptions[i] = descriptions[i] with
                {
                    ProvenanceUpdate = GenerationProvenanceUpdate.Append([provenance]),
                };
            }
        }

        IElementAdder adder = editViewModel.GetRequiredService<IElementAdder>();
        ElementAddResult? result = null;
        try
        {
            result = await adder.AddAsync(descriptions, cancellationToken);
            return new CaptionSceneImportResult(result.IsSuccess, result.Failure?.Id);
        }
        finally
        {
            // ElementAddResult retains its input descriptions, whose source factories may belong
            // to a collectible package. Clear both references before releasing the template lease.
            result = null;
            descriptions.Clear();
        }
    }
}
