using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class TimelineTabExtension : ToolTabExtension
{
    public static readonly TimelineTabExtension Instance = new();

    public override string Name => "Timeline";

    public override string DisplayName => "Timeline";

    public override bool CanMultiple => false;

    public override string? Header => Strings.Timeline;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition("Paste", "Paste", "Paste the copied data.",
        [
            new ContextCommandKeyGesture("Ctrl+V"),
            new ContextCommandKeyGesture("Cmd+V", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Rename", "Rename", "Rename the selected item.",
        [
            new ContextCommandKeyGesture("F2"),
            new ContextCommandKeyGesture("Enter", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Exclude", "Exclude", "Exclude the selected item.",
        [
            new ContextCommandKeyGesture("Delete"),
            new ContextCommandKeyGesture("Back", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Delete", "Delete", "Delete the selected item.",
        [
            new ContextCommandKeyGesture("Ctrl+Delete"),
            new ContextCommandKeyGesture("Cmd+Back", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Copy", "Copy", "Copy the selected item.",
        [
            new ContextCommandKeyGesture("Ctrl+C"),
            new ContextCommandKeyGesture("Cmd+C", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Cut", "Cut", "Cut the selected item.",
        [
            new ContextCommandKeyGesture("Ctrl+X"),
            new ContextCommandKeyGesture("Cmd+X", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Split", "Split", "Split by current frame.",
        [
            new ContextCommandKeyGesture("Ctrl+K"),
            new ContextCommandKeyGesture("Cmd+K", OSPlatform.OSX),
        ]),
    ];

    public override IconSource GetIcon()
    {
        return new SymbolIconSource() { Symbol = Symbol.GlanceHorizontal };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new Timeline();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new TimelineViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
