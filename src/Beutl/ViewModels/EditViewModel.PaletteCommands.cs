using Beutl.Editor.Components.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.ViewModels;

// Commands that take their arguments from the command palette's input and pick steps. A keyboard
// invocation has no interaction, so these stay unhandled there (GotoTimecode keeps its inline editor).
public partial class EditViewModel
{
    private async Task GotoTimecodeAsync(IContextCommandInteraction interaction)
    {
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Prompt = Strings.GotoTimecode_Description,
            Placeholder = Strings.GotoTimecode_InputHint,
            Value = Player.CurrentFrame.Value.ToString(CommandPaletteInput.TimecodeFormat),
            Validate = value => Player.TryParseTimecode(value, out _, out GotoTimecodeError error)
                ? null
                : PlayerViewModel.GetTimecodeErrorMessage(error)
        });
        // Relative input resolves against the playhead when it is confirmed, not when it was last validated.
        if (input is null || !Player.TryParseTimecode(input, out TimeSpan target, out _)) return;

        await SeekFromPaletteAsync(target);
    }

    private async Task GoToMarkerAsync(IContextCommandInteraction interaction)
    {
        SceneMarker? marker = await PickMarkerAsync(interaction);
        if (marker is null || !Scene.Markers.Contains(marker)) return;

        await SeekFromPaletteAsync(marker.Time);
    }

    private async Task AddMarkerAsync(IContextCommandInteraction interaction)
    {
        // The marker goes where the playhead was when the command ran, not where playback has moved it to since.
        int rate = Player.GetFrameRate();
        TimeSpan time = _editorClock.CurrentTime.Value.RoundToRate(rate);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        // A frame keeps one marker: Toggle marker removes only one of two markers sharing a frame,
        // so an existing one is renamed instead of duplicated.
        SceneMarker? existing = Scene.Markers.FirstOrDefault(m => m.Time.RoundToRate(rate) == time);
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = existing?.Name ?? $"Marker {Scene.Markers.Count + 1}",
            Validate = CommandPaletteInput.Required
        });
        if (name is null) return;

        if (existing is not null && Scene.Markers.Contains(existing))
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
                string time = m.Time.ToString(CommandPaletteInput.TimecodeFormat);
                return new ContextCommandPickItem<SceneMarker>(
                    string.IsNullOrWhiteSpace(m.Name) ? time : m.Name, m, time);
            })
            .ToArray();

        return (await interaction.ShowQuickPickAsync(items))?.Value;
    }

    // The playback ticker would overwrite a seek made while playing, so playback stops first.
    private async Task SeekFromPaletteAsync(TimeSpan time)
    {
        if (Player.IsPlaying.Value)
        {
            await Player.Pause();
        }

        SeekAndScroll(time);
    }
}
