namespace Beutl.Engine;

/// <summary>An effect whose editing handles sit on the picture it passes on, before the effects after it change it.</summary>
/// <remarks>
/// While the effect is selected for editing and the preview is paused, the host renders the preview without the effects
/// after it, so an effect that moves the picture later does not move it away from the handles. Only a filter effect has
/// effects after it.
/// </remarks>
public interface IEditingStageProvider : IEditingHandleProvider
{
    /// <summary>The note the preview shows while it hides the effects after this one.</summary>
    string EditingStageHint { get; }
}
