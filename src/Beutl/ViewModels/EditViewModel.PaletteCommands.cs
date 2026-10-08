using Beutl.Editor.Components.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.ViewModels;

// Commands that take their arguments from the command palette's input and pick steps. A keyboard
// invocation has no interaction, so these stay unhandled there (GotoTimecode keeps its inline editor).
public partial class EditViewModel
{
    private async Task GotoTimecodeAsync(IContextCommandInteraction interaction)
    {
        string initial = CommandPaletteInput.FormatTimecode(Player.CurrentFrame.Value);
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Prompt = Strings.GotoTimecode_Description,
            Placeholder = Strings.GotoTimecode_InputHint,
            Value = initial,
            Validate = value => Player.TryParseTimecode(value, out _, out GotoTimecodeError error)
                ? null
                : PlayerViewModel.GetTimecodeErrorMessage(error)
        });
        // Accepting the prefilled text keeps the playhead, as the inline editor does: milliseconds cannot
        // name every frame of a very high frame rate, so re-parsing it could land on a neighboring frame.
        if (input is null || string.Equals(input, initial, StringComparison.Ordinal)) return;

        // Relative input resolves against the playhead when it is confirmed, not when it was last validated.
        if (!Player.TryParseTimecode(input, out TimeSpan target, out _)) return;

        await SeekFromPaletteAsync(target, interaction.CancellationToken);
    }

    private async Task GoToMarkerAsync(IContextCommandInteraction interaction)
    {
        SceneMarker? marker = await PickMarkerAsync(interaction);
        if (marker is null || !Scene.Markers.Contains(marker)) return;

        await SeekFromPaletteAsync(marker.Time, interaction.CancellationToken);
    }

    private async Task AddMarkerAsync(IContextCommandInteraction interaction)
    {
        // The marker goes where the playhead was when the command ran, not where playback has moved it to since.
        int rate = Player.GetFrameRate();
        TimeSpan time = _editorClock.CurrentTime.Value.RoundToRate(rate);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        // A frame keeps one marker: Toggle marker removes only one of two markers sharing a frame,
        // so an existing one is renamed instead of duplicated.
        SceneMarker? FindMarkerOnFrame() => Scene.Markers.FirstOrDefault(m => m.Time.RoundToRate(rate) == time);
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = FindMarkerOnFrame()?.Name ?? $"Marker {Scene.Markers.Count + 1}",
            Validate = CommandPaletteInput.Required
        });
        if (name is null) return;

        // Looked up again: the markers may have changed while the name was being typed.
        if (FindMarkerOnFrame() is { } existing)
        {
            RenameMarkerCore(existing, name);
            return;
        }

        Scene.Markers.Add(new SceneMarker(time, name.Trim()));
        HistoryManager.Commit(CommandNames.AddMarker);
    }

    private async Task RenameMarkerAsync(IContextCommandInteraction interaction)
    {
        SceneMarker? marker = await PickMarkerAsync(interaction);
        if (marker is null) return;

        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = marker.Name,
            Validate = CommandPaletteInput.Required
        });
        if (name is null || !Scene.Markers.Contains(marker)) return;

        RenameMarkerCore(marker, name);
    }

    private void RenameMarkerCore(SceneMarker marker, string name)
    {
        name = name.Trim();
        if (marker.Name == name) return;

        marker.Name = name;
        HistoryManager.Commit(CommandNames.EditMarker);
    }

    private async Task<SceneMarker?> PickMarkerAsync(IContextCommandInteraction interaction)
    {
        ContextCommandPickItem<SceneMarker>[] items = Scene.Markers
            .OrderBy(m => m.Time)
            .Select(m =>
            {
                string time = CommandPaletteInput.FormatTimecode(m.Time);
                return new ContextCommandPickItem<SceneMarker>(
                    string.IsNullOrWhiteSpace(m.Name) ? time : m.Name, m, time);
            })
            .ToArray();

        return (await interaction.ShowQuickPickAsync(items))?.Value;
    }

    // The playback ticker would overwrite a seek made while playing, so playback stops first. Switching
    // editors cancels the command meanwhile, and then this editor must not move.
    private async Task SeekFromPaletteAsync(TimeSpan time, CancellationToken cancellationToken)
    {
        if (Player.IsPlaying.Value)
        {
            await Player.Pause();
        }

        if (cancellationToken.IsCancellationRequested) return;

        SeekAndScroll(time);
    }
}
