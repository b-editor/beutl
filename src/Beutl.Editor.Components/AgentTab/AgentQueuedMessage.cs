using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab;

internal sealed class AgentQueuedMessage(
    IEditorContext editor, AgentProviderDescriptor provider, string text, bool includeSceneContext,
    IReadOnlyList<AgentUserAttachment> attachments, string[] providerSkills, IReadOnlyList<AgentContextMention>? mentions)
{
    public IEditorContext Editor { get; } = editor;
    private string[] _providerSkills = providerSkills;
    public AgentProviderDescriptor Provider { get; private set; } = provider;
    public string Text { get; } = text;
    public string Preview { get; } = text.Split(['\r', '\n'], 2)[0];
    public bool IncludeSceneContext { get; } = includeSceneContext;
    public IReadOnlyList<AgentAttachedFile> Attachments { get; } =
        attachments.Select(attachment => new AgentAttachedFile(attachment.Path, attachment.MediaType)).ToArray();
    public IReadOnlyList<AgentContextMention>? Mentions { get; } = mentions;

    public AgentTurnRequest CreateRequest(Guid threadId, Guid turnId) => new(threadId, turnId, Text)
    {
        ProviderSkills = _providerSkills.Length > 0 ? _providerSkills : null,
        Attachments = attachments.Count > 0 ? attachments : null,
        IncludeSceneContext = IncludeSceneContext,
        Mentions = Mentions
    };

    public void SetProvider(AgentProviderDescriptor descriptor)
    {
        if (Provider.Id != descriptor.Id)
            _providerSkills = [];
        Provider = descriptor;
    }
}
