using System.Diagnostics.CodeAnalysis;

using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneEditorExtension : EditorExtension
{
    public static readonly SceneEditorExtension Instance = new();

    public override string[] FileExtensions { get; } =
    {
        Constants.SceneFileExtension
    };

    public override string FileTypeName => Strings.SceneFile;

    public override string Name => "Scene editor";

    public override string DisplayName => "Scene editor";

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out IEditor? editor)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}"))
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

    public override bool TryCreateContext(string file, [NotNullWhen(true)] out IEditorContext? context)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}")
            && ServiceLocator.Current.GetRequiredService<IWorkspaceItemContainer>().TryGetOrCreateItem(file, out Scene? model))
        {
            context = new EditViewModel(model);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Document
        };
    }
}
