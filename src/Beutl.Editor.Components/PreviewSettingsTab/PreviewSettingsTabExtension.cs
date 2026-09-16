using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.PreviewSettingsTab;

[PrimitiveImpl]
public sealed class PreviewSettingsTabExtension : ToolTabExtension
{
    public static readonly PreviewSettingsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Preview settings";

    public override string DisplayName => Strings.PreviewSettings;

    public override string Header => Strings.PreviewSettings;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.Options };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new PreviewSettingsTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new PreviewSettingsTabViewModel(editorContext);
        return true;
    }
}
