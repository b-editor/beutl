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

    public override string DisplayName => Strings.Timeline;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Timeline;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition("Paste", Strings.Paste, Strings.Paste_Description,
        [
            new ContextCommandKeyGesture("Ctrl+V"),
            new ContextCommandKeyGesture("Cmd+V", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Rename", Strings.Rename, Strings.Rename_Description,
        [
            new ContextCommandKeyGesture("F2"),
            new ContextCommandKeyGesture("Enter", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Exclude", Strings.Exclude, Strings.Exclude_Description,
        [
            new ContextCommandKeyGesture("Delete"),
            new ContextCommandKeyGesture("Back", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Delete", Strings.Delete, Strings.Delete_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Delete"),
            new ContextCommandKeyGesture("Cmd+Back", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Copy", Strings.Copy, Strings.Copy_Description,
        [
            new ContextCommandKeyGesture("Ctrl+C"),
            new ContextCommandKeyGesture("Cmd+C", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Cut", Strings.Cut, Strings.Cut_Description,
        [
            new ContextCommandKeyGesture("Ctrl+X"),
            new ContextCommandKeyGesture("Cmd+X", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("Split", Strings.SplitByCurrentFrame, "",
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
