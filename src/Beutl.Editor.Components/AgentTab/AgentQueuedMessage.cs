using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab;

internal sealed class AgentQueuedMessage(
    IEditorContext editor, AgentProviderDescriptor provider, string text, bool includeSceneContext,
    IReadOnlyList<AgentAttachedFile> attachments, string[] providerSkills, IReadOnlyList<AgentContextMention>? mentions)
{
    public IEditorContext Editor { get; } = editor;
    public AgentProviderDescriptor Provider { get; } = provider;
    public string Text { get; } = text;
    public string Preview { get; } = text.Split(['\r', '\n'], 2)[0];
    public bool IncludeSceneContext { get; } = includeSceneContext;
    public IReadOnlyList<AgentAttachedFile> Attachments { get; } = attachments;
    public IReadOnlyList<AgentContextMention>? Mentions { get; } = mentions;

    public AgentTurnRequest CreateRequest(Guid threadId, Guid turnId) => new(threadId, turnId, Text)
    {
        ProviderSkills = providerSkills.Length > 0 ? providerSkills : null
    };
}
