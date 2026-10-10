using Beutl.Api.Services;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;

namespace Beutl.AgentHost;

/// <summary>A timed piece of a transcript, in seconds from the start of the file.</summary>
public sealed record AgentTranscriptSegment(double Start, double End, string Text);

/// <summary>A word with its time, when the transcription model reports words.</summary>
public sealed record AgentTranscriptWord(double Start, double End, string Word);

public sealed record AgentTranscript(
    string? Language,
    IReadOnlyList<AgentTranscriptSegment> Segments,
    IReadOnlyList<AgentTranscriptWord>? Words);

/// <summary>
/// The application's AI services as the agent host's AI tools use them: the signed-in account,
/// its models, and the executor the AI nodes run on, so agents pay through the same safeguards.
/// </summary>
internal interface IAgentAiBackend
{
    /// <summary>Null when the account can run AI generations; otherwise why it cannot.</summary>
    Task<string?> GetUnavailableReasonAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the models' availability reflects the account. While the account's plan cannot be
    /// read, the catalog marks every model unavailable, which is not a refusal.
    /// </summary>
    bool KnowsModelAvailability { get; }

    /// <summary>An executor that keeps its results with <paramref name="scene"/>.</summary>
    IGenerativeNodeExecutor CreateExecutor(Scene scene);

    /// <summary>How many bytes of reference pictures one image generation may carry in all.</summary>
    Task<long> GetImageReferenceBudgetAsync(CancellationToken cancellationToken);

    Task<AgentTranscript> TranscribeAsync(
        string path,
        string? language,
        string? modelId,
        IProgress<string> progress,
        CancellationToken cancellationToken);
}

/// <summary>Stands in until the application has its API clients, and in hosts without them.</summary>
internal sealed class UnavailableAgentAiBackend : IAgentAiBackend
{
    public static UnavailableAgentAiBackend Instance { get; } = new();

    public Task<string?> GetUnavailableReasonAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>("AI generation is not available in this host.");

    public Task<IReadOnlyList<GenerativeModelInfo>> GetModelsAsync(string operationId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GenerativeModelInfo>>([]);

    public bool KnowsModelAvailability => false;

    public IGenerativeNodeExecutor CreateExecutor(Scene scene)
        => throw new InvalidOperationException("AI generation is not available in this host.");

    public Task<long> GetImageReferenceBudgetAsync(CancellationToken cancellationToken)
        => Task.FromResult(AiRequestLimits.MaxImageReferencesTotalBytes);

    public Task<AgentTranscript> TranscribeAsync(
        string path,
        string? language,
        string? modelId,
        IProgress<string> progress,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("AI generation is not available in this host.");
}
