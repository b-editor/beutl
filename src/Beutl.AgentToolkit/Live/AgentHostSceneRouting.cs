using System.Collections.Frozen;

namespace Beutl.AgentToolkit.Live;

/// <summary>
/// The headless tools whose counterpart inside a running editor takes a <c>sceneId</c> instead of
/// a file session. The editor's scene router and the installed server share this set, so the
/// schema the server advertises is exactly what the editor accepts.
/// </summary>
public static class AgentHostSceneRouting
{
    public const string SceneIdArgument = "sceneId";

    // Scene-bound tools: the editor requires sceneId on them.
    public static readonly FrozenSet<string> SceneRequired = FrozenSet.ToFrozenSet(
    [
        "read_document_summary", "read_document", "apply_edit", "duplicate_object",
        "plan_composition", "apply_composition", "measure_object_bounds",
        "undo", "redo", "read_history", "render_still", "render_storyboard",
        "measure_frame_differences", "export_video", "add_scene", "save_project"
    ], StringComparer.Ordinal);

    // Tools that accept sceneId but also work without one.
    public static readonly FrozenSet<string> SceneOptional = FrozenSet.ToFrozenSet(
    [
        "read_operation_status", "list_compositions", "render_composition_patch"
    ], StringComparer.Ordinal);

    public static bool AcceptsScene(string toolName)
        => SceneRequired.Contains(toolName) || SceneOptional.Contains(toolName);
}
