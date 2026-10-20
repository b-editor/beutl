using System.Collections.Frozen;

namespace Beutl.Api.Services;

// keymap.json keyed a command by "<namespace>.<type>.<name>" before command ids (#2886), and the id is the type's
// prefix and the camel-cased name.
internal static class ContextCommandLegacyKeys
{
    public static readonly FrozenDictionary<string, string> Ids = new (string Type, string Prefix, string[] Names)[]
    {
        ("Beutl.Editor.Components.LibraryTab.LibraryTabExtension", "library",
            ["AddElement", "AddElementFromTemplate"]),
        ("Beutl.Editor.Components.PreviewSettingsTab.PreviewSettingsTabExtension", "previewSettings",
            ["ChangePreviewRenderQuality"]),
        ("Beutl.Editor.Components.SceneSettingsTab.SceneSettingsTabExtension", "sceneSettings",
            ["ChangeSceneDuration", "ChangeSceneSize", "ChangeSceneStart"]),
        ("Beutl.Services.PrimitiveImpls.DockLayoutTabExtension", "dockLayout",
            ["ApplyDockLayout", "DeleteDockLayout", "RenameDockLayout", "SaveDockLayout"]),
        ("Beutl.Services.PrimitiveImpls.HistoryTabExtension", "history",
            ["JumpToHistory"]),
        ("Beutl.Services.PrimitiveImpls.MainViewExtension", "main",
            ["ChangeTheme", "CloseProject", "CommitVersion", "CreateNewFile", "CreateNewProject", "CreateToolTab",
            "EnableVersionControl", "Exit", "NextTab", "NextToolTab", "OpenFile", "OpenProject", "OpenRecentFile",
            "OpenRecentProject", "PreviousTab", "PreviousToolTab", "Redo", "Save", "SaveAll", "ShowCommandPalette",
            "Undo"]),
        ("Beutl.Services.PrimitiveImpls.SceneEditorExtension", "editor",
            ["AddMarker", "GoToMarker", "GotoTimecode", "Next", "NextKeyFrame", "NextMarker", "PlayPause", "Previous",
            "PreviousKeyFrame", "PreviousMarker", "RenameMarker", "SeekEnd", "SeekStart", "ShuttleBackward",
            "ShuttleBackwardFine", "ShuttleForward", "ShuttleForwardFine", "ShuttleStop", "ToggleLoop", "ToggleMarker",
            "ToggleOnionSkin"]),
        ("Beutl.Services.PrimitiveImpls.TimelineTabExtension", "timeline",
            ["CloseAllGaps", "CloseGap", "Copy", "Cut", "Delete", "Duplicate", "Exclude", "ExitRazorMode",
            "ExitRollMode", "ExitSlideMode", "ExitSlipMode", "GoToNextGap", "GoToPreviousGap", "NudgeLeftFrame",
            "NudgeLeftLarge", "NudgeLeftSecond", "NudgeRightFrame", "NudgeRightLarge", "NudgeRightSecond", "Paste",
            "Rename", "SaveAsTemplate", "SetEndTime", "SetStartTime", "Split", "ToggleGroup", "ToggleRazorMode",
            "ToggleRippleMode", "ToggleRollMode", "ToggleSlideMode", "ToggleSlipMode"]),
    }
        .SelectMany(group => group.Names.Select(name => KeyValuePair.Create(
            $"{group.Type}.{name}", $"{group.Prefix}.{char.ToLowerInvariant(name[0])}{name[1..]}")))
        .ToFrozenDictionary();
}
