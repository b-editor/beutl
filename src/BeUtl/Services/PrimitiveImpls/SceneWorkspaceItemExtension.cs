using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using Beutl.Controls;
using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneWorkspaceItemExtension : WorkspaceItemExtension
{
    public static readonly SceneWorkspaceItemExtension Instance = new();

    public override Geometry? Icon => FluentIconsRegular.Document.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        Constants.SceneFileExtension
    };

    public override IObservable<string> FileTypeName => Observable.Return(Strings.SceneFile);

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
