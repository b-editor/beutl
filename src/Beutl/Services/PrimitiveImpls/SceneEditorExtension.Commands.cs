using System.Runtime.InteropServices;
using System.Windows.Input;
using Beutl.Configuration;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.Services.PrimitiveImpls;

public sealed partial class SceneEditorExtension
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition<EditViewModel>("editor.playPause", Strings.PlayPause, Strings.PlayPause_Description,
            [new("Space")])
        {
            CanExecuteOn = (editor, execution) => !execution.IsFromTextInput && CanRun(editor.Player.PlayPause),
            ExecuteOnAsync = (editor, _) => editor.Player.PlayPause.ExecuteAsync()
        },
        PlayerCommand("editor.next", Strings.MoveToNext, Strings.MoveToNext_Description,
            [new("Right")], player => player.Next),
        PlayerCommand("editor.previous", Strings.MoveToPrevious, Strings.MoveToPrevious_Description,
            [new("Left")], player => player.Previous),
        PlayerCommand("editor.seekStart", Strings.MoveToStart, Strings.MoveToStart_Description,
            [new("Home"), new("Cmd+Left", OSPlatform.OSX)], player => player.Start),
        PlayerCommand("editor.seekEnd", Strings.MoveToEnd, Strings.MoveToEnd_Description,
            [new("End"), new("Cmd+Right", OSPlatform.OSX)], player => player.End),
        KeyCommand("editor.toggleMarker", Strings.ToggleMarker, Strings.ToggleMarker_Description,
            [new("M")], editor => editor.ToggleMarkerAtCurrentTime()),
        KeyCommand("editor.nextMarker", Strings.NextMarker, Strings.NextMarker_Description,
            [new("Ctrl+Right"), new("Alt+Right", OSPlatform.OSX)], editor => editor.SeekToAdjacentMarker(forward: true)),
        KeyCommand("editor.previousMarker", Strings.PreviousMarker, Strings.PreviousMarker_Description,
            [new("Ctrl+Left"), new("Alt+Left", OSPlatform.OSX)], editor => editor.SeekToAdjacentMarker(forward: false)),
        KeyCommand("editor.nextKeyFrame", Strings.NextKeyFrame, Strings.NextKeyFrame_Description,
            [new("Ctrl+Shift+Right"), new("Cmd+Shift+Right", OSPlatform.OSX)],
            editor => editor.SeekToAdjacentKeyFrame(forward: true)),
        KeyCommand("editor.previousKeyFrame", Strings.PreviousKeyFrame,
            Strings.PreviousKeyFrame_Description, [new("Ctrl+Shift+Left"), new("Cmd+Shift+Left", OSPlatform.OSX)],
            editor => editor.SeekToAdjacentKeyFrame(forward: false)),
        KeyCommand("editor.shuttleForward", Strings.ShuttleForward, Strings.ShuttleForward_Description,
            [new("L")], editor => editor.Player.ShuttleForward()),
        KeyCommand("editor.shuttleForwardFine", Strings.ShuttleForwardFine,
            Strings.ShuttleForwardFine_Description, [new("Shift+L")], editor => editor.Player.ShuttleForward(fineGrain: true)),
        KeyCommand("editor.shuttleBackward", Strings.ShuttleBackward, Strings.ShuttleBackward_Description,
            [new("J")], editor => editor.Player.ShuttleBackward()),
        KeyCommand("editor.shuttleBackwardFine", Strings.ShuttleBackwardFine,
            Strings.ShuttleBackwardFine_Description, [new("Shift+J")], editor => editor.Player.ShuttleBackward(fineGrain: true)),
        KeyCommand("editor.shuttleStop", Strings.ShuttleStop, Strings.ShuttleStop_Description,
            [new("K")], editor => editor.Player.ShuttleStop()),
        KeyCommand("editor.toggleLoop", Strings.ToggleLoop, Strings.ToggleLoop_Description,
            [new("OemQuestion")], editor => editor.Player.ToggleLoop()),
        new ContextCommandDefinition<EditViewModel>("editor.gotoTimecode", Strings.GotoTimecode, Strings.GotoTimecode_Description,
            [new("G")])
        {
            // The palette asks for the timecode; a key opens the player's inline editor instead.
            CanExecuteOn = (_, execution) => execution.Interaction is not null || !execution.IsFromTextInput,
            ExecuteOnAsync = (editor, execution) =>
            {
                if (execution.Interaction is { } interaction)
                    return editor.GotoTimecodeAsync(interaction);

                editor.Player.RequestEditTimecode();
                return Task.CompletedTask;
            }
        },
        KeyCommand("editor.toggleOnionSkin", Strings.ToggleOnionSkin, Strings.ToggleOnionSkin_Description,
            [new("Alt+O")], _ => ToggleOnionSkin()),
        // The timeline's Close gap shares the key and runs first while the timeline is focused with a clip selected.
        KeyCommand("editor.togglePreviewGuides", Strings.TogglePreviewGuides, Strings.TogglePreviewGuides_Description,
            [new("OemSemicolon")], _ => TogglePreviewGuides()),
        // Palette-only: they ask for their arguments, so they have no default gesture.
        PaletteCommand("editor.goToMarker", Strings.GoToMarker,
            (editor, interaction) => editor.GoToMarkerAsync(interaction), editor => editor.Scene.Markers.Count > 0),
        PaletteCommand("editor.addMarker", Strings.AddMarkerWithName,
            (editor, interaction) => editor.AddMarkerAsync(interaction)),
        PaletteCommand("editor.renameMarker", Strings.RenameMarker,
            (editor, interaction) => editor.RenameMarkerAsync(interaction), editor => editor.Scene.Markers.Count > 0),
    ];

    private static bool CanRun(ICommand command) => command.CanExecute(null);

    private static ContextCommandDefinition<EditViewModel> PlayerCommand(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Func<PlayerViewModel, ReactiveCommand> command)
        => new(id, displayName, description, keyGestures)
        {
            CanExecuteOn = (editor, _) => CanRun(command(editor.Player)),
            ExecuteOnAsync = (editor, _) =>
            {
                command(editor.Player).Execute();
                return Task.CompletedTask;
            }
        };

    // Bound to plain keys, so a key typed into a text box stays there.
    private static ContextCommandDefinition<EditViewModel> KeyCommand(
        string id, string displayName, string description, ContextCommandKeyGesture[] keyGestures,
        Action<EditViewModel> execute)
        => new(id, displayName, description, keyGestures)
        {
            CanExecuteOn = (_, execution) => !execution.IsFromTextInput,
            ExecuteOnAsync = (editor, _) =>
            {
                execute(editor);
                return Task.CompletedTask;
            }
        };

    private static ContextCommandDefinition<EditViewModel> PaletteCommand(
        string id, string displayName, Func<EditViewModel, IContextCommandInteraction, Task> execute,
        Func<EditViewModel, bool>? canExecute = null)
        => new(id, displayName, "", [])
        {
            CanExecuteOn = canExecute is null ? null : (editor, _) => canExecute(editor),
            ExecuteOnAsync = (editor, execution) =>
                execution.Interaction is { } interaction ? execute(editor, interaction) : Task.CompletedTask
        };

    private static void ToggleOnionSkin()
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        editorConfig.IsOnionSkinEnabled.Value = !editorConfig.IsOnionSkinEnabled.Value;
    }

    private static void TogglePreviewGuides()
    {
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        editorConfig.IsPreviewGuideEnabled.Value = !editorConfig.IsPreviewGuideEnabled.Value;
    }
}
