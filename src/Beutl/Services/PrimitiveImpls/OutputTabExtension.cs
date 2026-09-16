using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OutputTabExtension : ToolTabExtension
{
    public static readonly OutputTabExtension Instance = new();

    public override string Name => "Output";

    public override string DisplayName => Strings.Output;

    public override string? Header => Strings.Output;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 1;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.ArrowExport };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new OutputTab();
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
            context = new OutputTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
