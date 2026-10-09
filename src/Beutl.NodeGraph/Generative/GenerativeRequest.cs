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

    /// <summary>For a request no node makes, such as one started from the timeline.</summary>
    protected GenerativeRequest(string catalogOperationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(catalogOperationId);
        CatalogOperationId = catalogOperationId;
    }

    /// <summary>The node that made the request, or null when it was made outside a graph.</summary>
    public GenerativeNode? Node { get; }

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

    // Up to 80 characters of a prompt; a longer one is cut to 79 and an ellipsis.
    internal static string Abbreviate(string prompt)
        => prompt.Length <= 80 ? prompt : string.Concat(prompt.AsSpan(0, 79), "…");
}

public sealed record AiImageGenerationNodeRequest : GenerativeRequest
{
    public AiImageGenerationNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public AiImageGenerationNodeRequest(string catalogOperationId) : base(catalogOperationId)
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

    public override string Summary => Abbreviate(Prompt);
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

    /// <summary>
    /// The canvas an outpaint sends: the picture with <paramref name="expansionPercent"/> of
    /// its width added on the left and right, and of its height above and below.
    /// </summary>
    public static (int Width, int Height, int Horizontal, int Vertical) GetOutpaintDimensions(
        int sourceWidth,
        int sourceHeight,
        int expansionPercent)
    {
        if (expansionPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(expansionPercent));
        int horizontal = Math.Max(1, checked((int)Math.Round(sourceWidth * expansionPercent / 100d)));
        int vertical = Math.Max(1, checked((int)Math.Round(sourceHeight * expansionPercent / 100d)));
        return (checked(sourceWidth + horizontal * 2), checked(sourceHeight + vertical * 2), horizontal, vertical);
    }
}

public sealed record AiImageEditNodeRequest : GenerativeRequest
{
    public AiImageEditNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public AiImageEditNodeRequest(string catalogOperationId) : base(catalogOperationId)
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
public sealed record GenerativeExecutionResult(Uri ResultFile, string? ModelId, int? Seed, bool IsVideo = false);

public enum AiVideoEditMode
{
    [Display(Name = nameof(Strings.AiVideoEditing), ResourceType = typeof(Strings))]
    Edit,
    [Display(Name = nameof(Strings.AiVideoExtend), ResourceType = typeof(Strings))]
    Extend,
    [Display(Name = nameof(Strings.AiVideoMotion), ResourceType = typeof(Strings))]
    Motion,
}

public enum AiMotionOrientation
{
    [Display(Name = nameof(Strings.AiSourceVideo), ResourceType = typeof(Strings))]
    Video,
    [Display(Name = nameof(Strings.AiCharacterImage), ResourceType = typeof(Strings))]
    Image,
}

public enum AiMotionQuality
{
    [Display(Name = nameof(Strings.AiMotionStandard), ResourceType = typeof(Strings))]
    Standard,
    [Display(Name = nameof(Strings.AiMotionPro), ResourceType = typeof(Strings))]
    Pro,
}

public sealed record AiVideoEditNodeRequest : GenerativeRequest
{
    public AiVideoEditNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public AiVideoEditNodeRequest(string catalogOperationId) : base(catalogOperationId)
    {
    }

    public override GenerativeOperation Operation => GenerativeOperation.VideoEdit;

    public required AiVideoEditMode Mode { get; init; }

    public required string Prompt { get; init; }

    /// <summary>How long to extend by, or to generate for motion; unused by an edit.</summary>
    public int DurationSeconds { get; init; }

    public required GenerativeFileInput SourceVideo { get; init; }

    public GenerativeImageInput? CharacterImage { get; init; }

    public AiMotionOrientation Orientation { get; init; }

    public AiMotionQuality Quality { get; init; }

    public override string Fingerprint => GenerativeFingerprint.Combine(
        [nameof(GenerativeOperation.VideoEdit), ParameterFingerprint, SourceVideo.ContentHash, CharacterImage?.ContentHash]);

    public override string Summary => Abbreviate(Prompt);
}

/// <summary>A media file handed to a generation as input, read at queue time.</summary>
public sealed record GenerativeFileInput(string Name, string MediaType, byte[] Content)
{
    public string ContentHash { get; } = GenerativeFingerprint.Hash(Content);
}

public sealed record AiVideoGenerationNodeRequest : GenerativeRequest
{
    public AiVideoGenerationNodeRequest(GenerativeNode node) : base(node)
    {
    }

    public AiVideoGenerationNodeRequest(string catalogOperationId) : base(catalogOperationId)
    {
    }

    public override GenerativeOperation Operation => GenerativeOperation.VideoGeneration;

    public required string Prompt { get; init; }

    public required int DurationSeconds { get; init; }

    public required string Resolution { get; init; }

    public required string AspectRatio { get; init; }

    public bool GenerateAudio { get; init; }

    public int? Seed { get; init; }

    public GenerativeImageInput? FirstFrame { get; init; }

    public GenerativeImageInput? LastFrame { get; init; }

    public IReadOnlyList<GenerativeImageInput> ImageReferences { get; init; } = [];

    public IReadOnlyList<GenerativeFileInput> VideoReferences { get; init; } = [];

    public override string Fingerprint => GenerativeFingerprint.Combine(
        [
            nameof(GenerativeOperation.VideoGeneration),
            ParameterFingerprint,
            FirstFrame?.ContentHash,
            LastFrame?.ContentHash,
            .. ImageReferences.Select(reference => reference.ContentHash),
            .. VideoReferences.Select(reference => reference.ContentHash),
        ]);

    public override string Summary => Abbreviate(Prompt);
}

/// <summary>A report from a running generation: what it is doing and a rough picture.</summary>
/// <remarks>The receiver owns <see cref="Preview"/> and must dispose it.</remarks>
public sealed record GenerativeProgress(string? Status, Ref<Bitmap>? Preview = null);

/// <summary>
/// A generation that failed with something to tell the person who queued it. The
/// message is already localized by the executor.
/// </summary>
public sealed class GenerativeExecutionException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// The server settled the request (failed and refunded, or gone), so the idempotency key
    /// it went out under would only ever answer with this failure; the next attempt needs a
    /// new one. False when the key is still the way back to a paid result.
    /// </summary>
    public bool SettledRequest { get; init; }
}

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
