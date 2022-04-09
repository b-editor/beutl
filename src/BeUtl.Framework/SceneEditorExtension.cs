using BeUtl.ProjectSystem;

namespace BeUtl.Framework;

public abstract class SceneEditorExtension : ViewExtension
{
    public enum TabPlacement
    {
        Bottom,
        Right
    }

    public abstract TabPlacement Placement { get; }

    public abstract bool IsClosable { get; }

    public abstract ResourceReference<string> Header { get; }

    public abstract IEditor CreateContent(Scene scene);
}
