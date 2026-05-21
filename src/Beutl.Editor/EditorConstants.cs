namespace Beutl.Editor;

/// <summary>
/// File-extension and folder-name constants used across the editor
/// projects (<c>Beutl.Editor</c>, <c>Beutl.Editor.Components</c>, and the
/// app). Single source of truth — call sites in every layer reach for
/// this type, no duplicated literals.
/// </summary>
public static class EditorConstants
{
    public const string ElementFileExtension = "belm";

    public const string SceneFileExtension = "scene";

    public const string ProjectFileExtension = "bep";

    public const string BeutlFolder = ".beutl";

    public const string ViewStateFolder = "view-state";

    public const string ProjectPackageExtension = "beutl";
}
