using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed partial class SceneEditorExtension : EditorExtension
{
    public static readonly SceneEditorExtension Instance = new();

    public override string Name => "Scene editor";

    public override string DisplayName => Strings.Editor;

    public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
    {
        if (obj is Scene)
        {
            editor = new EditView();
            return true;
        }
        else
        {
            editor = null;
            return false;
        }
    }

    public override bool TryCreateContext(
        CoreObject obj, IEditorContextServices services, [NotNullWhen(true)] out IEditorContext? context)
    {
        // TryCreate* must not throw: when the host does not supply the services EditViewModel needs,
        // fail (return false) rather than pushing nulls into EditViewModel.
        if (obj is Scene scene
            && services.TryGetService<EditorService>(out EditorService? editorService))
        {
            context = new EditViewModel(scene, editorService.ExtensionProvider, editorService);
            return true;
        }

        context = null;
        return false;
    }

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.Document };
    }

    public override FilePickerFileType GetFilePickerFileType()
    {
        return CreateSceneFileType();
    }

    // Also used by SceneOutputExtension.
    internal static FilePickerFileType CreateSceneFileType()
    {
        return new FilePickerFileType(Strings.SceneFile) { Patterns = [$"*.{EditorConstants.SceneFileExtension}"] };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext.Equals($".{EditorConstants.SceneFileExtension}", StringComparison.OrdinalIgnoreCase);
    }
}
