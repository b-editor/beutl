using Avalonia.Media;

using BeUtl.ProjectSystem;

namespace BeUtl.Framework;

public abstract class SceneEditorTabExtension : ViewExtension
{
    public enum TabPlacement
    {
        Bottom,
        Right
    }

    public abstract Geometry? Icon { get; }

    public abstract TabPlacement Placement { get; }

    public abstract bool IsClosable { get; }

    public abstract ResourceReference<string> Header { get; }

    public abstract object CreateContent(Scene scene);
}
