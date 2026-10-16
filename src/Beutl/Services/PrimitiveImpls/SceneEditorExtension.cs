using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed class SceneEditorExtension : EditorExtension
{
    public static readonly SceneEditorExtension Instance = new();

    public override string Name => "Scene editor";

    public override string DisplayName => Strings.Editor;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("PlayPause", Strings.PlayPause, Strings.PlayPause_Description,
        [
            new ContextCommandKeyGesture("Space")
        ]) { Id = "editor.playPause" },
        new("Next", Strings.MoveToNext, Strings.MoveToNext_Description,
        [
            new ContextCommandKeyGesture("Right")
        ]) { Id = "editor.next" },
        new("Previous", Strings.MoveToPrevious, Strings.MoveToPrevious_Description,
        [
            new ContextCommandKeyGesture("Left")
        ]) { Id = "editor.previous" },
        new("SeekStart", Strings.MoveToStart, Strings.MoveToStart_Description,
        [
            new ContextCommandKeyGesture("Home"),
            new ContextCommandKeyGesture("Cmd+Left", OSPlatform.OSX),
        ]) { Id = "editor.seekStart" },
        new("SeekEnd", Strings.MoveToEnd, Strings.MoveToEnd_Description,
        [
            new ContextCommandKeyGesture("End"),
            new ContextCommandKeyGesture("Cmd+Right", OSPlatform.OSX)
        ]) { Id = "editor.seekEnd" },
        new("ToggleMarker", Strings.ToggleMarker, Strings.ToggleMarker_Description,
        [
            new ContextCommandKeyGesture("M")
        ]) { Id = "editor.toggleMarker" },
        new("NextMarker", Strings.NextMarker, Strings.NextMarker_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Right"),
            new ContextCommandKeyGesture("Alt+Right", OSPlatform.OSX)
        ]) { Id = "editor.nextMarker" },
        new("PreviousMarker", Strings.PreviousMarker, Strings.PreviousMarker_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Left"),
            new ContextCommandKeyGesture("Alt+Left", OSPlatform.OSX)
        ]) { Id = "editor.previousMarker" },
        new("NextKeyFrame", Strings.NextKeyFrame, Strings.NextKeyFrame_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+Right"),
            new ContextCommandKeyGesture("Cmd+Shift+Right", OSPlatform.OSX)
        ]) { Id = "editor.nextKeyFrame" },
        new("PreviousKeyFrame", Strings.PreviousKeyFrame, Strings.PreviousKeyFrame_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+Left"),
            new ContextCommandKeyGesture("Cmd+Shift+Left", OSPlatform.OSX)
        ]) { Id = "editor.previousKeyFrame" },
        new("ShuttleForward", Strings.ShuttleForward, Strings.ShuttleForward_Description,
        [
            new ContextCommandKeyGesture("L")
        ]) { Id = "editor.shuttleForward" },
        new("ShuttleForwardFine", Strings.ShuttleForwardFine, Strings.ShuttleForwardFine_Description,
        [
            new ContextCommandKeyGesture("Shift+L")
        ]) { Id = "editor.shuttleForwardFine" },
        new("ShuttleBackward", Strings.ShuttleBackward, Strings.ShuttleBackward_Description,
        [
            new ContextCommandKeyGesture("J")
        ]) { Id = "editor.shuttleBackward" },
        new("ShuttleBackwardFine", Strings.ShuttleBackwardFine, Strings.ShuttleBackwardFine_Description,
        [
            new ContextCommandKeyGesture("Shift+J")
        ]) { Id = "editor.shuttleBackwardFine" },
        new("ShuttleStop", Strings.ShuttleStop, Strings.ShuttleStop_Description,
        [
            new ContextCommandKeyGesture("K")
        ]) { Id = "editor.shuttleStop" },
        new("ToggleLoop", Strings.ToggleLoop, Strings.ToggleLoop_Description,
        [
            new ContextCommandKeyGesture("OemQuestion")
        ]) { Id = "editor.toggleLoop" },
        new("GotoTimecode", Strings.GotoTimecode, Strings.GotoTimecode_Description,
        [
            new ContextCommandKeyGesture("G")
        ]) { Id = "editor.gotoTimecode" },
        new("ToggleOnionSkin", Strings.ToggleOnionSkin, Strings.ToggleOnionSkin_Description,
        [
            new ContextCommandKeyGesture("Alt+O")
        ]) { Id = "editor.toggleOnionSkin" },
        // Palette-only: they ask for their arguments, so they have no default gesture.
        new("GoToMarker", Strings.GoToMarker, "", []) { Id = "editor.goToMarker" },
        new("AddMarker", Strings.AddMarkerWithName, "", []) { Id = "editor.addMarker" },
        new("RenameMarker", Strings.RenameMarker, "", []) { Id = "editor.renameMarker" },
    ];

    public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
    {
        if (obj is Scene)
        {
            editor = new EditView();
            return true;
        }
        else
        {
            editor = null;
            return false;
        }
    }

    public override bool TryCreateContext(
        CoreObject obj, IEditorContextServices services, [NotNullWhen(true)] out IEditorContext? context)
    {
        // TryCreate* must not throw: when the host does not supply the services EditViewModel needs,
        // fail (return false) rather than pushing nulls into EditViewModel.
        if (obj is Scene scene
            && services.TryGetService<EditorService>(out EditorService? editorService))
        {
            context = new EditViewModel(scene, editorService.ExtensionProvider, editorService);
            return true;
        }

        context = null;
        return false;
    }

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.Document };
    }

    public override FilePickerFileType GetFilePickerFileType()
    {
        return CreateSceneFileType();
    }

    // Also used by SceneOutputExtension.
    internal static FilePickerFileType CreateSceneFileType()
    {
        return new FilePickerFileType(Strings.SceneFile) { Patterns = [$"*.{EditorConstants.SceneFileExtension}"] };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext.Equals($".{EditorConstants.SceneFileExtension}", StringComparison.OrdinalIgnoreCase);
    }
}
