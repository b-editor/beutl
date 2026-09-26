using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.NodeGraph.Generative;

/// <summary>The kind of work a generative node asks the executor to do.</summary>
public enum GenerativeOperation
{
    ImageGeneration,
    ImageEdit,
    VideoGeneration,
    VideoEdit,
}

/// <summary>
/// A picture handed to a generation as input, already rendered and encoded, so the
/// executor never touches the render pipeline.
/// </summary>
public sealed record GenerativeImageInput(string Name, byte[] EncodedPng)
{
    public string ContentHash { get; } = GenerativeFingerprint.Hash(EncodedPng);
}

/// <summary>
/// Everything one run of a generative node needs, materialized at queue time.
/// </summary>
public abstract record GenerativeRequest
{
    protected GenerativeRequest(GenerativeNode node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        CatalogOperationId = node.CatalogOperationId;
    }

    public GenerativeNode Node { get; }

    /// <summary>The operation whose models apply, captured with the rest of the inputs.</summary>
    public string CatalogOperationId { get; init; }

    public abstract GenerativeOperation Operation { get; }

    /// <summary>The model to run on, or null for the server's default.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Stable per node and persisted, so the idempotency key of a request that never
    /// reported back is the same after a restart and the paid result is recovered.
    /// </summary>
    public required string RequestKeySeed { get; init; }

    public required string ParameterFingerprint { get; init; }

    /// <summary>Identifies the complete request, inputs included.</summary>
    public abstract string Fingerprint { get; }

    /// <summary>A short description for the generation history.</summary>
    public abstract string Summary { get; }
}

public sealed record AiImageGenerationNodeRequest : GenerativeRequest
{
    public AiImageGenerationNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public override GenerativeOperation Operation => GenerativeOperation.ImageGeneration;

    public required string Prompt { get; init; }

    public required string AspectRatio { get; init; }

    public string Background { get; init; } = "auto";

    public int? Seed { get; init; }

    public IReadOnlyList<GenerativeImageInput> References { get; init; } = [];

    public override string Fingerprint => GenerativeFingerprint.Combine(
        [
            nameof(GenerativeOperation.ImageGeneration),
            ParameterFingerprint,
            .. References.Select(reference => reference.ContentHash),
        ]);

    public override string Summary => Prompt.Length <= 80 ? Prompt : string.Concat(Prompt.AsSpan(0, 79), "…");
}

public enum AiImageEditTask
{
    [Display(Name = nameof(Strings.AiEditRemoveBackground), ResourceType = typeof(Strings))]
    RemoveBackground,
    [Display(Name = nameof(Strings.AiEditUpscale), ResourceType = typeof(Strings))]
    Upscale,
    [Display(Name = nameof(Strings.AiEditRestyle), ResourceType = typeof(Strings))]
    Restyle,
    [Display(Name = nameof(Strings.AiEditRemoveObject), ResourceType = typeof(Strings))]
    RemoveObject,
    [Display(Name = nameof(Strings.AiEditOutpaint), ResourceType = typeof(Strings))]
    Outpaint,
}

public enum AiOutpaintExpansion
{
    [Display(Name = "10%")]
    Percent10,
    [Display(Name = "25%")]
    Percent25,
    [Display(Name = "50%")]
    Percent50,
}

public static class AiImageEditTasks
{
    /// <summary>The task as the server names it.</summary>
    public static string ToId(this AiImageEditTask task) => task switch
    {
        AiImageEditTask.RemoveBackground => "remove_background",
        AiImageEditTask.Upscale => "upscale",
        AiImageEditTask.Restyle => "restyle",
        AiImageEditTask.RemoveObject => "remove_object",
        AiImageEditTask.Outpaint => "outpaint",
        _ => throw new ArgumentOutOfRangeException(nameof(task)),
    };

    /// <summary>The tasks the AI tab asks a prompt for.</summary>
    public static bool RequiresPrompt(this AiImageEditTask task)
        => task is AiImageEditTask.Restyle or AiImageEditTask.RemoveObject or AiImageEditTask.Outpaint;

    public static int ToPercent(this AiOutpaintExpansion expansion) => expansion switch
    {
        AiOutpaintExpansion.Percent10 => 10,
        AiOutpaintExpansion.Percent50 => 50,
        _ => 25,
    };
}

public sealed record AiImageEditNodeRequest : GenerativeRequest
{
    public AiImageEditNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public override GenerativeOperation Operation => GenerativeOperation.ImageEdit;

    public required AiImageEditTask Task { get; init; }

    /// <summary>The user's prompt, or null for a task that takes none.</summary>
    public string? Prompt { get; init; }

    public int? OutpaintExpansionPercent { get; init; }

    public required GenerativeImageInput Image { get; init; }

    public override string Fingerprint => GenerativeFingerprint.Combine(
        [nameof(GenerativeOperation.ImageEdit), ParameterFingerprint, Image.ContentHash]);

    public override string Summary
    {
        get
        {
            string task = Beutl.TypeDisplayHelpers.GetLocalizedName(typeof(AiImageEditTask).GetField(Task.ToString())!);
            return string.IsNullOrWhiteSpace(Prompt) ? task : $"{task}: {Prompt}";
        }
    }
}

/// <summary>What the executor produced: a file it saved and the parameters that made it.</summary>
public sealed record GenerativeExecutionResult(Uri ResultFile, string? ModelId, int? Seed);

/// <summary>A report from a running generation: what it is doing and a rough picture.</summary>
/// <remarks>The receiver owns <see cref="Preview"/> and must dispose it.</remarks>
public sealed record GenerativeProgress(string? Status, Ref<Bitmap>? Preview = null);

/// <summary>
/// A generation that failed with something to tell the person who queued it. The
/// message is already localized by the executor.
/// </summary>
public sealed class GenerativeExecutionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public static class GenerativeFingerprint
{
    public static string Hash(ReadOnlySpan<byte> content)
        => Convert.ToHexStringLower(SHA256.HashData(content));

    public static string Combine(IEnumerable<string?> parts)
    {
        var builder = new StringBuilder();
        foreach (string? part in parts)
        {
            // Length-prefixed so that ("ab", "c") and ("a", "bc") differ.
            string value = part ?? "\0";
            builder.Append(value.Length).Append(':').Append(value).Append('|');
        }

        return Hash(Encoding.UTF8.GetBytes(builder.ToString()));
    }
}
