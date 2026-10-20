using Beutl.AgentHost;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

// An AI backend for hosts in tests that have no API clients: every AI tool reports itself unavailable.
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

    public Task<bool> RequiresPromptAsync(string imageEditTask, CancellationToken cancellationToken)
        => Task.FromResult(AiRequestRules.RequiresPrompt(null, imageEditTask));

    public Task<AgentTranscript> TranscribeAsync(
        string path,
        string? language,
        string? modelId,
        IProgress<string> progress,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("AI generation is not available in this host.");
}
