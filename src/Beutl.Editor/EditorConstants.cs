namespace Beutl.Editor;

/// <summary>
/// File-extension and folder-name constants shared across the editor projects.
/// Single source of truth so no layer duplicates the literals.
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
