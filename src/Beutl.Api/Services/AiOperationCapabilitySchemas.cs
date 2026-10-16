namespace Beutl.Api.Services;

/// <summary>Identifies the supported model-capability shape for an AI operation.</summary>
public enum AiModelCapabilitySchema
{
    Generic,
    Image,
    Video,
}

internal static class AiOperationCapabilitySchemas
{
    public static AiModelCapabilitySchema Get(AiOperationId operation)
    {
        if (operation == AiOperations.VideoGeneration || operation == AiOperations.VideoEditing
            || operation == AiOperations.VideoExtension || operation == AiOperations.VideoMotion)
            return AiModelCapabilitySchema.Video;
        if (operation == AiOperations.ImageGeneration
            || operation.Value.StartsWith("image.edit.", StringComparison.Ordinal))
        {
            return AiModelCapabilitySchema.Image;
        }

        return AiModelCapabilitySchema.Generic;
    }
}
