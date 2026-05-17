using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class TimelineTabExtension : ToolTabExtension
{
    public static readonly TimelineTabExtension Instance = new();

    public override string Name => "Timeline";

    public override string DisplayName => Strings.Timeline;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Timeline;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 0;

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
        new ContextCommandDefinition("SetStartTime", Strings.SetStartTime, "",
        [
            new ContextCommandKeyGesture("OemOpenBrackets")
        ]),
        new ContextCommandDefinition("SetEndTime", Strings.SetEndTime, "",
        [
            new ContextCommandKeyGesture("OemCloseBrackets")
        ]),
        new ContextCommandDefinition("NudgeLeftFrame", Strings.NudgeLeftFrame, "",
        [
            new ContextCommandKeyGesture("Left"),
        ]),
        new ContextCommandDefinition("NudgeRightFrame", Strings.NudgeRightFrame, "",
        [
            new ContextCommandKeyGesture("Right"),
        ]),
        new ContextCommandDefinition("NudgeLeftLarge", Strings.NudgeLeftLarge, "",
        [
            new ContextCommandKeyGesture("Shift+Left"),
        ]),
        new ContextCommandDefinition("NudgeRightLarge", Strings.NudgeRightLarge, "",
        [
            new ContextCommandKeyGesture("Shift+Right"),
        ]),
        // macOS の Cmd+Left/Right はシーン側 SeekStart/SeekEnd と衝突するため
        // 明示バインドはしない (fallback の Alt+Left/Right が Opt+Left/Right として
        // 利く)。Opt+Left/Right はマーカー間ナビと重なるが、ContextCommandManager の
        // input element ルーティングで Timeline フォーカス時のみ nudge が走るため
        // 実害は限定的。
        new ContextCommandDefinition("NudgeLeftSecond", Strings.NudgeLeftSecond, "",
        [
            new ContextCommandKeyGesture("Alt+Left"),
        ]),
        new ContextCommandDefinition("NudgeRightSecond", Strings.NudgeRightSecond, "",
        [
            new ContextCommandKeyGesture("Alt+Right"),
        ]),
        new ContextCommandDefinition("ToggleGroup", Strings.Group, "",
        [
            new ContextCommandKeyGesture("Ctrl+G"),
            new ContextCommandKeyGesture("Cmd+G", OSPlatform.OSX),
        ]),
        new ContextCommandDefinition("ToggleRazorMode", Strings.RazorTool, Strings.RazorTool_Description,
        [
            new ContextCommandKeyGesture("C"),
        ]),
        new ContextCommandDefinition("ExitRazorMode", Strings.ExitRazorTool, Strings.ExitRazorTool_Description,
        [
            new ContextCommandKeyGesture("V"),
            new ContextCommandKeyGesture("Escape"),
        ]),
    ];

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new TimelineTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new TimelineTabViewModel(editorContext);
        return true;
    }
}
