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
        GenerativeOperation operation,
        CancellationToken cancellationToken)
        => (await LoadAsync(operation, cancellationToken)).Models;

    internal async Task<(AiModelCatalog Catalog, IReadOnlyList<GenerativeModelInfo> Models)> LoadAsync(
        GenerativeOperation operation,
        CancellationToken cancellationToken)
    {
        AiModelCatalog loaded = await catalog.GetAsync(cancellationToken);
        AiOperationId id = ToOperationId(operation);
        AiEntitlements? current = entitlements.Entitlements.Value;
        // Only a reported refusal rules the operation out, as in the picker.
        bool operationIsAvailable = current is not null
            && current.Availability.GetState(id) != AiOperationAvailabilityState.Unavailable;
        var models = new List<GenerativeModelInfo>();
        foreach (AiModelOption model in loaded.ModelsFor(id))
        {
            // A model that takes no picture cannot generate from one.
            if (model.Image is { } image && !image.CanServeAnything(false))
                continue;

            bool available = current?.ModelAvailability.CanStart(id, model.Id, operationIsAvailable) ?? false;
            models.Add(new GenerativeModelInfo(
                model.Id.Value,
                new AiModelPickerOption(model, available).ToString(),
                model.IsDefault,
                available,
                ToCapabilities(model.Image)));
        }

        return (loaded, models);
    }

    internal static AiOperationId ToOperationId(GenerativeOperation operation) => operation switch
    {
        GenerativeOperation.ImageGeneration => AiOperations.ImageGeneration,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

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
