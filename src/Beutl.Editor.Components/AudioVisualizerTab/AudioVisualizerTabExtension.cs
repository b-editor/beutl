using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.AudioVisualizerTab.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.AudioVisualizerTab;

[PrimitiveImpl]
public sealed class AudioVisualizerTabExtension : ToolTabExtension
{
    public static readonly AudioVisualizerTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Audio Visualizer Tab";

    public override string DisplayName => Strings.AudioVisualizer;

    public override string Header => Strings.AudioVisualizer;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new AudioVisualizerTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new AudioVisualizerTabViewModel(editorContext, Instance);
        return true;
    }
}
