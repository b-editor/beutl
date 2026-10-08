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
public sealed class MissingMediaTabExtension : ToolTabExtension
{
    public static readonly MissingMediaTabExtension Instance = new();

    public override string Name => "Missing Media";
    public override string DisplayName => MissingMediaStrings.Title;
    public override bool CanMultiple => false;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;
    public override FAIconSource? GetIcon() => new FluentIconSource { Icon = Icon.Link };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = editorContext is EditViewModel ? new MissingMediaView() : null;
        return control != null;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = editorContext is EditViewModel editor ? new MissingMediaViewModel(editor) : null;
        return context != null;
    }
}
