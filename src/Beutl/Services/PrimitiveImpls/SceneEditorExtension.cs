using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
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
        ]),
        new("Next", Strings.MoveToNext, Strings.MoveToNext_Description,
        [
            new ContextCommandKeyGesture("Right")
        ]),
        new("Previous", Strings.MoveToPrevious, Strings.MoveToPrevious_Description,
        [
            new ContextCommandKeyGesture("Left")
        ]),
        new("SeekStart", Strings.MoveToStart, Strings.MoveToStart_Description,
        [
            new ContextCommandKeyGesture("Home"),
            new ContextCommandKeyGesture("Cmd+Left", OSPlatform.OSX),
        ]),
        new("SeekEnd", Strings.MoveToEnd, Strings.MoveToEnd_Description,
        [
            new ContextCommandKeyGesture("End"),
            new ContextCommandKeyGesture("Cmd+Right", OSPlatform.OSX)
        ]),
        new("ToggleMarker", Strings.ToggleMarker, Strings.ToggleMarker_Description,
        [
            new ContextCommandKeyGesture("M")
        ]),
        new("NextMarker", Strings.NextMarker, Strings.NextMarker_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Right"),
            new ContextCommandKeyGesture("Alt+Right", OSPlatform.OSX)
        ]),
        new("PreviousMarker", Strings.PreviousMarker, Strings.PreviousMarker_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Left"),
            new ContextCommandKeyGesture("Alt+Left", OSPlatform.OSX)
        ]),
        new("ShuttleForward", "Shuttle forward", "Shuttle forward (L). Press repeatedly to accelerate.",
        [
            new ContextCommandKeyGesture("L")
        ]),
        new("ShuttleForwardFine", "Shuttle forward (fine)", "Shuttle forward at half speed.",
        [
            new ContextCommandKeyGesture("Shift+L")
        ]),
        new("ShuttleBackward", "Shuttle backward", "Shuttle backward (J). Press repeatedly to accelerate.",
        [
            new ContextCommandKeyGesture("J")
        ]),
        new("ShuttleBackwardFine", "Shuttle backward (fine)", "Shuttle backward at half speed.",
        [
            new ContextCommandKeyGesture("Shift+J")
        ]),
        new("ShuttleStop", "Shuttle stop", "Stop shuttle playback.",
        [
            new ContextCommandKeyGesture("K")
        ]),
        new("SetInPoint", "Set In point", "Set the In point at the current playhead position.",
        [
            new ContextCommandKeyGesture("I")
        ]),
        new("SetOutPoint", "Set Out point", "Set the Out point at the current playhead position.",
        [
            new ContextCommandKeyGesture("O")
        ]),
        new("ToggleLoop", "Toggle loop", "Toggle loop playback.",
        [
            new ContextCommandKeyGesture("OemQuestion"),
            new ContextCommandKeyGesture("OemSlash")
        ]),
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

    public override bool TryCreateContext(CoreObject obj, [NotNullWhen(true)] out IEditorContext? context)
    {
        if (obj is Scene scene)
        {
            context = new EditViewModel(scene);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Document };
    }

    public override FilePickerFileType GetFilePickerFileType()
    {
        return new FilePickerFileType(Strings.SceneFile) { Patterns = ["*.scene"] };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext.Equals(".scene", StringComparison.OrdinalIgnoreCase);
    }
}
