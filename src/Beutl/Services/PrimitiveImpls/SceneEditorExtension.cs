using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Models;
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
    ];

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out Control? editor)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}", StringComparison.OrdinalIgnoreCase))
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

    public override bool TryCreateContext(string file, [NotNullWhen(true)] out IEditorContext? context)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}", StringComparison.OrdinalIgnoreCase)
            && ProjectItemContainer.Current.TryGetOrCreateItem(file, out Scene? model))
        {
            context = new EditViewModel(model);
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
