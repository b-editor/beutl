using Beutl.Api.Services;
using Beutl.NodeGraph.Generative;
using Beutl.ViewModels;

namespace Beutl.Services.AI;

/// <summary>
/// The model catalog as the node graph sees it, decided the way the AI dialogs' model
/// picker decides it: the same filter, the same availability and the same labels.
/// </summary>
internal sealed class AiGenerativeModelCatalog(
    IAiModelCatalogService catalog,
    IAiEntitlementService entitlements) : IGenerativeModelCatalog
{
    public async Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(
        string operationId,
        CancellationToken cancellationToken)
        => (await LoadAsync(operationId, cancellationToken)).Models;

    internal async Task<(AiModelCatalog Catalog, IReadOnlyList<GenerativeModelInfo> Models)> LoadAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        AiModelCatalog loaded = await catalog.GetAsync(cancellationToken);
        var id = new AiOperationId(operationId);
        AiEntitlements? current = entitlements.Entitlements.Value;
        // Only a reported refusal rules the operation out, as in the picker.
        bool operationIsAvailable = current is not null
            && current.Availability.GetState(id) != AiOperationAvailabilityState.Unavailable;
        var models = new List<GenerativeModelInfo>();
        foreach (AiModelOption model in loaded.ModelsFor(id))
        {
            if (!IsOffered(operationId, model))
                continue;

            bool available = current?.ModelAvailability.CanStart(id, model.Id, operationIsAvailable) ?? false;
            models.Add(new GenerativeModelInfo(
                model.Id.Value,
                new AiModelPickerOption(model, available).ToString(),
                model.IsDefault,
                available,
                ToCapabilities(model.Image),
                ToVideoCapabilities(model.Video)));
        }

        return (loaded, models);
    }

    /// <summary>The filters the AI dialogs put on their model pickers, per operation.</summary>
    internal static bool IsOffered(string operationId, AiModelOption model)
    {
        if (operationId.StartsWith("image.edit.", StringComparison.Ordinal))
        {
            // Every edit hands the model a picture; an upscale also asks for a size, and
            // removing a background asks for a transparent one.
            string task = operationId["image.edit.".Length..];
            return model.Image is not { } edit
                || edit.CanServeAnything(
                    requiresReferenceImages: true,
                    requiresResolution: task == "upscale",
                    requiredBackground: task == "remove_background" ? "transparent" : null);
        }

        if (operationId.StartsWith("video.", StringComparison.Ordinal))
            return model.Video is not { } video || video.CanServeAnything();

        // A model that takes no picture cannot generate from one.
        return model.Image is not { } image || image.CanServeAnything(false);
    }

    private static GenerativeVideoCapabilities ToVideoCapabilities(AiVideoModelCapabilities? video)
    {
        AiVideoModelCapabilities c = video ?? AiVideoModelCapabilities.Unrestricted;
        return new GenerativeVideoCapabilities(
            c.DurationsSeconds.IsSpecified ? [.. c.DurationsSeconds.Values] : null,
            c.Resolutions.IsSpecified ? [.. c.Resolutions.Values] : null,
            c.AspectRatios.IsSpecified ? [.. c.AspectRatios.Values] : null,
            c.SupportsAudio,
            c.SupportsSeed,
            c.SupportsFirstFrame,
            // A last frame is only ever sent alongside a first one.
            c.SupportsFirstFrame && c.SupportsLastFrame,
            c.SupportsPromptToVideo,
            c.SupportsInputReferences,
            c.MaxInputReferences,
            c.MaxInputReferenceBytes,
            c.MaxVideoReferences,
            c.MaxVideoReferenceBytes,
            c.MaxPromptLength);
    }

    private static GenerativeImageCapabilities ToCapabilities(AiImageModelCapabilities? image)
    {
        AiImageModelCapabilities capabilities = image ?? AiImageModelCapabilities.Unrestricted;
        return new GenerativeImageCapabilities(
            capabilities.AspectRatios.IsSpecified ? [.. capabilities.AspectRatios.Values] : null,
            capabilities.Backgrounds.IsSpecified ? [.. capabilities.Backgrounds.Values] : null,
            capabilities.SupportsSeed,
            // The price covers a fixed count; whichever is smaller may actually be sent.
            Math.Clamp(capabilities.MaxReferenceImages, 0, AiRequestLimits.MaxImageReferences));
    }
}
