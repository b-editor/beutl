namespace Beutl.Editor.Components.AgentTab;

internal interface IAgentNotificationSoundPlayer
{
    Task PlayAsync(CancellationToken cancellationToken);
}
