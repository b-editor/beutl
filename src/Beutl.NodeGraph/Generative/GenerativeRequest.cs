using System.Security.Cryptography;
using System.Text;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.NodeGraph.Generative;

/// <summary>The kind of work a generative node asks the executor to do.</summary>
public enum GenerativeOperation
{
    ImageGeneration,
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
    }

    public GenerativeNode Node { get; }

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
