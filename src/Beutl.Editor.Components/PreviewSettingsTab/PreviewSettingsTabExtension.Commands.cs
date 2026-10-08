using Beutl.Editor.Components.Converters;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PreviewSettingsTab;

// Picks the preview render quality from the palette, without opening the preview settings tab.
public sealed partial class PreviewSettingsTabExtension : IContextCommandHandler
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("ChangePreviewRenderQuality", Strings.ChangePreviewRenderQuality, "", [])
        {
            Scope = ContextCommandScope.Extension
        },
    ];

    // Unlike the tab's selector, the command stays available during playback: it pauses before rebuilding
    // the renderer, and a palette entry disabled by playback would not re-enable when playback stops.
    public bool CanExecute(ContextCommandExecution execution)
    {
        return execution.CommandName == "ChangePreviewRenderQuality"
            && execution.EditorContext?.GetService<IPreviewRenderQuality>() is not null;
    }

    public async Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (!CanExecute(execution)
            || execution.Interaction is not { } interaction
            || execution.EditorContext?.GetService<IPreviewRenderQuality>() is not { } quality)
        {
            return;
        }

        ContextCommandPickItem<RenderScale>[] items = quality.PreviewScaleOptions
            .Select(scale => new ContextCommandPickItem<RenderScale>(
                RenderScaleNameConverter.GetName(scale),
                scale,
                CommandPaletteInput.DescribeCurrent(scale == quality.PreviewScale.Value)))
            .ToArray();
        // Confirming the quality already in use changes nothing, so it must not stop playback either.
        if (await interaction.ShowQuickPickAsync(items) is not { } picked
            || picked.Value == quality.PreviewScale.Value)
        {
            return;
        }

        // Rebuilding the renderer must not happen mid-playback. Pause also returns a pause still draining
        // the last frame, which IsPlaying no longer shows.
        if (execution.EditorContext.GetService<IPreviewPlayer>() is { } player)
        {
            await player.Pause();
        }

        // Switching editors while pausing cancels the command; the editor left behind keeps its quality.
        if (interaction.CancellationToken.IsCancellationRequested) return;

        quality.PreviewScale.Value = picked.Value;
    }
}
