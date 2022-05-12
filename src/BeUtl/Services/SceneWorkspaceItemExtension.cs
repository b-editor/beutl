using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.ProjectSystem;

namespace BeUtl.Services;

[PrimitiveImpl]
public sealed class SceneWorkspaceItemExtension : WorkspaceItemExtension
{
    public static readonly SceneWorkspaceItemExtension Instance = new();

    public override Geometry? Icon { get; } = FluentIconsRegular.Document.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        "scene"
    };

    public override ResourceReference<string> FileTypeName => "S.Common.SceneFile";

    public override string Name => "Make the scene a workspace item.";

    public override string DisplayName => "Make the scene a workspace item.";

    public override bool TryCreateItem(string file, [NotNullWhen(true)] out IWorkspaceItem? result)
    {
        result = null;
        if (file.EndsWith(".scene"))
        {
            Scene? scene;
            try
            {
                scene = new Scene();
                scene.Restore(file);
            }
            catch
            {
                return false;
            }
            result = scene;
            return true;
        }
        else
        {
            return false;
        }
    }
}
