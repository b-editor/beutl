using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

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
        new("NextKeyFrame", Strings.NextKeyFrame, Strings.NextKeyFrame_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+Right"),
            new ContextCommandKeyGesture("Cmd+Shift+Right", OSPlatform.OSX)
        ]),
        new("PreviousKeyFrame", Strings.PreviousKeyFrame, Strings.PreviousKeyFrame_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Shift+Left"),
            new ContextCommandKeyGesture("Cmd+Shift+Left", OSPlatform.OSX)
        ]),
        new("ShuttleForward", Strings.ShuttleForward, Strings.ShuttleForward_Description,
        [
            new ContextCommandKeyGesture("L")
        ]),
        new("ShuttleForwardFine", Strings.ShuttleForwardFine, Strings.ShuttleForwardFine_Description,
        [
            new ContextCommandKeyGesture("Shift+L")
        ]),
        new("ShuttleBackward", Strings.ShuttleBackward, Strings.ShuttleBackward_Description,
        [
            new ContextCommandKeyGesture("J")
        ]),
        new("ShuttleBackwardFine", Strings.ShuttleBackwardFine, Strings.ShuttleBackwardFine_Description,
        [
            new ContextCommandKeyGesture("Shift+J")
        ]),
        new("ShuttleStop", Strings.ShuttleStop, Strings.ShuttleStop_Description,
        [
            new ContextCommandKeyGesture("K")
        ]),
        new("ToggleLoop", Strings.ToggleLoop, Strings.ToggleLoop_Description,
        [
            new ContextCommandKeyGesture("OemQuestion")
        ]),
        new("GotoTimecode", Strings.GotoTimecode, Strings.GotoTimecode_Description,
        [
            new ContextCommandKeyGesture("G")
        ]),
        new("ToggleOnionSkin", Strings.ToggleOnionSkin, Strings.ToggleOnionSkin_Description,
        [
            new ContextCommandKeyGesture("Alt+O")
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

    public override bool TryCreateContext(
        CoreObject obj, IEditorContextServices services, [NotNullWhen(true)] out IEditorContext? context)
    {
        // TryCreate* must not throw: only build the context for the host-provided services we
        // recognize, otherwise fail (return false) rather than pushing nulls into EditViewModel.
        if (obj is Scene scene && services is EditorContextServices ctx)
        {
            context = new EditViewModel(scene, ctx.ExtensionProvider, ctx.EditorService);
            return true;
        }

        context = null;
        return false;
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
