using System.Diagnostics.CodeAnalysis;

using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneWorkspaceItemExtension : WorkspaceItemExtension
{
    public static readonly SceneWorkspaceItemExtension Instance = new();

    public override string[] FileExtensions { get; } =
    {
        Constants.SceneFileExtension
    };

    public override string FileTypeName => Strings.SceneFile;

    public override string Name => "Make the scene a workspace item.";

    public override string DisplayName => "Make the scene a workspace item.";

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Document
        };
    }

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
