using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneWorkspaceItemExtension : WorkspaceItemExtension
{
    public static readonly SceneWorkspaceItemExtension Instance = new();

    public override Geometry? Icon { get; } = FluentIconsRegular.Document.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        Constants.SceneFileExtension
    };

    public override ResourceReference<string> FileTypeName => "S.Common.SceneFile";

    public override string Name => "Make the scene a workspace item.";

    public override string DisplayName => "Make the scene a workspace item.";

    public override bool TryCreateItem(string file, [NotNullWhen(true)] out IWorkspaceItem? result)
    {
        result = null;
        if (file.EndsWith($".{Constants.SceneFileExtension}"))
        {
            Scene? scene;
            try
            {
                scene = new Scene();
                scene.Restore(file);
            }
            catch
            {
                Debug.Fail("Unable to restore the scene.");
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
