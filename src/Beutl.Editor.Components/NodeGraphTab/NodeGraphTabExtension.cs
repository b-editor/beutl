using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.NodeGraphTab;

public sealed class NodeGraphTabExtension : ToolTabExtension
{
    public static readonly NodeGraphTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "NodeGraphTab";

    public override string DisplayName => Strings.NodeGraph;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override string? Header => Strings.NodeGraph;

    internal const string DeleteCommandId = "nodeGraph.delete";

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new ContextCommandDefinition<NodeGraphView>(DeleteCommandId, Strings.Delete, "",
            [new("Delete"), new("Delete", OSPlatform.OSX), new("Back", OSPlatform.OSX)])
        {
            CanExecuteOn = (_, execution) => execution.KeyEventArgs is not null && !execution.IsFromTextInput,
            ExecuteOnAsync = (view, execution) =>
            {
                var key = execution.KeyEventArgs!;
                key.Handled = view.DeleteSelectedNodes(key.Source);
                return Task.CompletedTask;
            }
        }
    ];

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.Flow };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new NodeGraphTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new NodeGraphTabViewModel(editorContext);
        return true;
    }
}
