using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    internal event Action? FocusRequested;
    internal bool HasPendingComposerFocus { get; set; }

    internal async Task PopulateComposerAsync(IReadOnlyList<AgentContextMention>? mentions, bool captureFrame, string? filePath)
    {
        await InitializeAsync();
        // A thread change can still be restoring its draft. Apply this input to the draft once it is ready.
        while (_managementCompletion is { } pending)
            await pending.Task;

        await ManageAsync(async ct =>
        {
            if (mentions is { Count: > 0 })
            {
                string text = Prompt ?? string.Empty;
                string separator = text.Length == 0 || char.IsWhiteSpace(text[^1]) ? string.Empty : " ";
                var tokens = mentions.Select(mention => RegisterMention(mention, MentionToken(mention)));
                Prompt = text + separator + string.Join(' ', tokens) + " ";
            }
            if (captureFrame)
            {
                await CaptureFrameIntoDraftAsync(ct);
                IncludeSceneContext = true;
            }
            if (filePath is not null)
                await AddAttachmentAsync(new AgentAttachedFile(filePath), ct);
        });

        if (!_disposed)
        {
            HasPendingComposerFocus = true;
            FocusRequested?.Invoke();
        }
    }
}
