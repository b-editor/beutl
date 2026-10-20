using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.MissingMediaTab.ViewModels;
using Beutl.Editor.Components.MissingMediaTab.Views;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.MissingMediaTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:MissingMediaTabExtension")]
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
        control = CanRepair(editorContext) ? new MissingMediaView() : null;
        return control != null;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = CanRepair(editorContext) ? new MissingMediaViewModel(editorContext, []) : null;
        if (context is MissingMediaViewModel tool) _ = tool.InitializeRowsAsync();
        return context != null;
    }

    private static bool CanRepair(IEditorContext editorContext)
        => editorContext.GetService(typeof(IMediaRepairHost)) is IMediaRepairHost;
}
