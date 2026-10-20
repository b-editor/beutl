using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.OutputTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:OutputTabExtension")]
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
        if (CanOutput(editorContext))
        {
            control = new OutputTabView();
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
        if (CanOutput(editorContext))
        {
            context = new OutputTabViewModel(editorContext);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }

    private static bool CanOutput(IEditorContext editorContext)
        => editorContext.GetService(typeof(Scene)) is Scene
           && editorContext.GetService(typeof(IOutputRunHost)) is IOutputRunHost
           && editorContext.GetService(typeof(IOutputPresetStore)) is IOutputPresetStore;
}
