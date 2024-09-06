using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

public sealed class SceneSettingsTabExtension : ToolTabExtension
{
    public static readonly SceneSettingsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Scene settings";

    public override string DisplayName => Name;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.PlaySettings };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new SceneSettingsTab();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new SceneSettingsTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
