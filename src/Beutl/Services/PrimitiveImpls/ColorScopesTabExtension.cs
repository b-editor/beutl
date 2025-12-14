using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ColorScopesTabExtension : ToolTabExtension
{
    public static readonly ColorScopesTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Color Scopes Tab";

    public override string DisplayName => Strings.ColorScopes;

    public override string Header => Strings.ColorScopes;

    public override IconSource GetIcon()
    {
        return new PathIconSource
        {
            Data = Geometry.Parse("M4 12a8 8 0 1 1 16 0a8 8 0 0 1-16 0Zm8-6a6 6 0 1 0 0 12a6 6 0 0 0 0-12Z")
        };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new ColorScopesTab();
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
            context = new ColorScopesTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
