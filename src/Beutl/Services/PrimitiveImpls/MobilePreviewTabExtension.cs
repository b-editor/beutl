using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed class MobilePreviewTabExtension : ToolTabExtension
{
    public static readonly MobilePreviewTabExtension Instance = new();

    public override string Name => "Mobile preview";
    public override string DisplayName => Strings.MobilePreview;
    public override string? Header => null;
    public override bool CanMultiple => false;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override FAIconSource? GetIcon() => new FluentIconSource { Icon = Icon.Phone };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = editorContext is EditViewModel ? new MobilePreviewTabView() : null;
        return control is not null;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = editorContext is EditViewModel editor ? new MobilePreviewTabViewModel(editor) : null;
        return context is not null;
    }
}
