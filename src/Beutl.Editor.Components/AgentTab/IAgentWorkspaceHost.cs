using System.ComponentModel;
using Avalonia.Media.Imaging;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab;

internal interface IAgentWorkspaceHost
{
    AgentRuntimeManager Runtime { get; }
    AgentThreadSessionHub ThreadSessions { get; }
    IProperty<bool> FollowLiveMcpEdits { get; }
    string DefaultProviderId { get; }
    AgentPermissionMode DefaultPermissionMode { get; }
    string? DefaultModel { get; }
    string? DefaultReasoningEffort { get; }
    bool SteerOnSend { get; }
    AgentComposerSendKey ComposerSendKey { get; }
    event EventHandler<PropertyChangedEventArgs>? RuntimeSettingsChanged;
    IReadOnlyList<AgentProviderDescriptor> GetProviders();
    IReadOnlyList<AgentComposerSuggestion> BundledSkills { get; }
    AgentTabViewModel? CreateWorkspace(IEditorContext editor);
    Task<IReadOnlyList<AgentMentionCandidate>> GetMentionCandidatesAsync(
        IEditorContext editor, string projectPath, CancellationToken cancellationToken);

    Task<AgentTurnSummary> RunTurnAsync(
        IEditorContext editor, string projectPath, AgentProviderDescriptor provider, AgentTurnRequest request,
        bool includeSceneContext, IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions,
        AgentTurnCallbacks callbacks,
        CancellationToken cancellationToken);

    Task<bool> SteerTurnAsync(
        IEditorContext editor, string projectPath, AgentTurnRequest request, bool includeSceneContext,
        IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions, CancellationToken cancellationToken);

    Task<AgentThreadMetadata> CompactContextAsync(
        string projectPath, Guid threadId, AgentProviderDescriptor provider, AgentTurnCallbacks callbacks, CancellationToken cancellationToken)
        => Runtime.CompactContextAsync(projectPath, threadId, provider, [], callbacks, cancellationToken);

    Task<AgentAttachedFile> SavePastedImageAsync(Bitmap image, CancellationToken cancellationToken);
    Task<AgentAttachedFile> SavePastedTextAsync(string text, CancellationToken cancellationToken);
    Task<AgentAttachedFile> CaptureFrameAsync(IEditorContext editor, CancellationToken cancellationToken);
    bool CanRevertTurn(IEditorContext editor, Guid turnId);
    Task<AgentTurnRevertResult> RevertTurnAsync(IEditorContext editor, Guid turnId, CancellationToken cancellationToken);
}

internal sealed record AgentMentionCandidate(AgentContextMention Mention, string Detail);

internal enum AgentTurnRevertResult
{
    Reverted,
    HistoryChanged,
    Unavailable
}
