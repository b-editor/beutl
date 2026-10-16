using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed partial class TimelineTabExtension : ToolTabExtension
{
    public static readonly TimelineTabExtension Instance = new();

    public override string Name => "Timeline";

    public override string DisplayName => Strings.Timeline;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Timeline;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 0;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.GlanceHorizontal };
    }

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition("Paste", Strings.Paste, Strings.Paste_Description,
        [
            new ContextCommandKeyGesture("Ctrl+V"),
            new ContextCommandKeyGesture("Cmd+V", OSPlatform.OSX),
        ]) { Id = "timeline.paste" },
        // Rename and Split act on the editor's selected element, so the palette runs them through this
        // extension even with the timeline closed. Their gestures still reach the focused clip.
        new ContextCommandDefinition("Rename", Strings.Rename, Strings.Rename_Description,
        [
            new ContextCommandKeyGesture("F2"),
            new ContextCommandKeyGesture("Enter", OSPlatform.OSX),
        ])
        {
            Id = "timeline.rename",
            Scope = ContextCommandScope.Extension
        },
        new ContextCommandDefinition("Exclude", Strings.Exclude, Strings.Exclude_Description,
        [
            new ContextCommandKeyGesture("Delete"),
            new ContextCommandKeyGesture("Back", OSPlatform.OSX),
        ]) { Id = "timeline.exclude" },
        new ContextCommandDefinition("Delete", Strings.Delete, Strings.Delete_Description,
        [
            new ContextCommandKeyGesture("Ctrl+Delete"),
            new ContextCommandKeyGesture("Cmd+Back", OSPlatform.OSX),
        ]) { Id = "timeline.delete" },
        new ContextCommandDefinition("Copy", Strings.Copy, Strings.Copy_Description,
        [
            new ContextCommandKeyGesture("Ctrl+C"),
            new ContextCommandKeyGesture("Cmd+C", OSPlatform.OSX),
        ]) { Id = "timeline.copy" },
        new ContextCommandDefinition("Cut", Strings.Cut, Strings.Cut_Description,
        [
            new ContextCommandKeyGesture("Ctrl+X"),
            new ContextCommandKeyGesture("Cmd+X", OSPlatform.OSX),
        ]) { Id = "timeline.cut" },
        new ContextCommandDefinition("Split", Strings.SplitByCurrentFrame, "",
        [
            new ContextCommandKeyGesture("Ctrl+K"),
            new ContextCommandKeyGesture("Cmd+K", OSPlatform.OSX),
        ])
        {
            Id = "timeline.split",
            Scope = ContextCommandScope.Extension
        },
        new ContextCommandDefinition("SaveAsTemplate", Strings.SaveAsTemplate, "", [])
        {
            Id = "timeline.saveAsTemplate",
            Scope = ContextCommandScope.Extension
        },
        new ContextCommandDefinition("Duplicate", Strings.Duplicate, Strings.Duplicate_Description,
        [
            new ContextCommandKeyGesture("Ctrl+D"),
            new ContextCommandKeyGesture("Cmd+D", OSPlatform.OSX),
        ]) { Id = "timeline.duplicate" },
        new ContextCommandDefinition("SetStartTime", Strings.SetStartTime, "",
        [
            new ContextCommandKeyGesture("OemOpenBrackets")
        ]) { Id = "timeline.setStartTime" },
        new ContextCommandDefinition("SetEndTime", Strings.SetEndTime, "",
        [
            new ContextCommandKeyGesture("OemCloseBrackets")
        ]) { Id = "timeline.setEndTime" },
        // Nudge uses , and . so that it does not collide with the existing shortcuts on the arrow keys.
        new ContextCommandDefinition("NudgeLeftFrame", Strings.NudgeLeftFrame, "",
        [
            new ContextCommandKeyGesture("OemComma"),
        ]) { Id = "timeline.nudgeLeftFrame" },
        new ContextCommandDefinition("NudgeRightFrame", Strings.NudgeRightFrame, "",
        [
            new ContextCommandKeyGesture("OemPeriod"),
        ]) { Id = "timeline.nudgeRightFrame" },
        new ContextCommandDefinition("NudgeLeftLarge", Strings.NudgeLeftLarge, "",
        [
            new ContextCommandKeyGesture("Shift+OemComma"),
        ]) { Id = "timeline.nudgeLeftLarge" },
        new ContextCommandDefinition("NudgeRightLarge", Strings.NudgeRightLarge, "",
        [
            new ContextCommandKeyGesture("Shift+OemPeriod"),
        ]) { Id = "timeline.nudgeRightLarge" },
        new ContextCommandDefinition("NudgeLeftSecond", Strings.NudgeLeftSecond, "",
        [
            new ContextCommandKeyGesture("Alt+OemComma"),
        ]) { Id = "timeline.nudgeLeftSecond" },
        new ContextCommandDefinition("NudgeRightSecond", Strings.NudgeRightSecond, "",
        [
            new ContextCommandKeyGesture("Alt+OemPeriod"),
        ]) { Id = "timeline.nudgeRightSecond" },
        new ContextCommandDefinition("ToggleGroup", Strings.Group, "",
        [
            new ContextCommandKeyGesture("Ctrl+G"),
            new ContextCommandKeyGesture("Cmd+G", OSPlatform.OSX),
        ]) { Id = "timeline.toggleGroup" },
        new ContextCommandDefinition("ToggleRazorMode", Strings.RazorTool, Strings.RazorTool_Description,
        [
            new ContextCommandKeyGesture("C"),
        ]) { Id = "timeline.toggleRazorMode" },
        new ContextCommandDefinition("ExitRazorMode", Strings.ExitRazorTool, Strings.ExitRazorTool_Description,
        [
            new ContextCommandKeyGesture("V"),
            new ContextCommandKeyGesture("Escape"),
        ]) { Id = "timeline.exitRazorMode" },
        new ContextCommandDefinition("ToggleRippleMode", Strings.RippleEdit, "",
        [
            new ContextCommandKeyGesture("B"),
        ]) { Id = "timeline.toggleRippleMode" },
        new ContextCommandDefinition("ToggleSlipMode", Strings.SlipTool, Strings.SlipTool_Description,
        [
            new ContextCommandKeyGesture("S"),
        ]) { Id = "timeline.toggleSlipMode" },
        new ContextCommandDefinition("ExitSlipMode", Strings.ExitSlipTool, Strings.ExitSlipTool_Description,
        [
            new ContextCommandKeyGesture("V"),
            new ContextCommandKeyGesture("Escape"),
        ]) { Id = "timeline.exitSlipMode" },
        new ContextCommandDefinition("ToggleRollMode", Strings.RollTool, Strings.RollTool_Description,
        [
            new ContextCommandKeyGesture("R"),
        ]) { Id = "timeline.toggleRollMode" },
        new ContextCommandDefinition("ExitRollMode", Strings.ExitRollTool, Strings.ExitRollTool_Description,
        [
            new ContextCommandKeyGesture("V"),
            new ContextCommandKeyGesture("Escape"),
        ]) { Id = "timeline.exitRollMode" },
        new ContextCommandDefinition("ToggleSlideMode", Strings.SlideTool, Strings.SlideTool_Description,
        [
            new ContextCommandKeyGesture("D"),
        ]) { Id = "timeline.toggleSlideMode" },
        new ContextCommandDefinition("ExitSlideMode", Strings.ExitSlideTool, Strings.ExitSlideTool_Description,
        [
            new ContextCommandKeyGesture("V"),
            new ContextCommandKeyGesture("Escape"),
        ]) { Id = "timeline.exitSlideMode" },
        new ContextCommandDefinition("CloseGap", Strings.CloseGap, "",
        [
            new ContextCommandKeyGesture("OemSemicolon"),
        ]) { Id = "timeline.closeGap" },
        new ContextCommandDefinition("CloseAllGaps", Strings.CloseAllGaps, "",
        [
            new ContextCommandKeyGesture("Shift+Alt+OemSemicolon"),
        ]) { Id = "timeline.closeAllGaps" },
        new ContextCommandDefinition("GoToNextGap", Strings.GoToNextGap, "",
        [
            new ContextCommandKeyGesture("Shift+OemSemicolon"),
        ]) { Id = "timeline.goToNextGap" },
        new ContextCommandDefinition("GoToPreviousGap", Strings.GoToPreviousGap, "",
        [
            new ContextCommandKeyGesture("Alt+OemSemicolon"),
        ]) { Id = "timeline.goToPreviousGap" },
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
