using System.Collections.Frozen;

namespace Beutl.AgentToolkit.Live;

/// <summary>
/// The tools that take a <c>sceneId</c> inside a running editor: headless tools whose counterpart
/// there takes a scene instead of a file session, and the editor's own AI generation tools. The
/// editor's scene router and the installed server share this set, so the schema the server
/// advertises is exactly what the editor accepts.
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

    // Tools that accept sceneId but also work without one. The AI generation tools exist only in the
    // editor; they save next to the scene sceneId names, or the selected tab's scene without one.
    public static readonly FrozenSet<string> SceneOptional = FrozenSet.ToFrozenSet(
    [
        "read_operation_status", "list_compositions", "render_composition_patch",
        "generate_image", "edit_image", "generate_video", "edit_video"
    ], StringComparer.Ordinal);

    public static bool AcceptsScene(string toolName)
        => SceneRequired.Contains(toolName) || SceneOptional.Contains(toolName);
}
