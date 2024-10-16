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

    public override string DisplayName => "Scene editor";

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("PlayPause", "Play/Pause", "Switches between play and pause.",
        [
            new ContextCommandKeyGesture("Space")
        ]),
        new("Next", "Next", "Move to the next frame.",
        [
            new ContextCommandKeyGesture("Right")
        ]),
        new("Previous", "Previous", "Move to the previous frame.",
        [
            new ContextCommandKeyGesture("Left")
        ]),
        new("SeekStart", "Seek start", "Move to the start of the scene.",
        [
            new ContextCommandKeyGesture("Home"),
            new ContextCommandKeyGesture("Cmd+Left", OSPlatform.OSX),
        ]),
        new("SeekEnd", "Seek end", "Move to the end of the scene.",
        [
            new ContextCommandKeyGesture("End"),
            new ContextCommandKeyGesture("Cmd+Right", OSPlatform.OSX)
        ]),
    ];

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out Control? editor)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}"))
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
        if (file.EndsWith($".{Constants.SceneFileExtension}")
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
        return new FilePickerFileType(Strings.SceneFile) { Patterns = new string[] { "*.scene" } };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext is ".scene";
    }
}
