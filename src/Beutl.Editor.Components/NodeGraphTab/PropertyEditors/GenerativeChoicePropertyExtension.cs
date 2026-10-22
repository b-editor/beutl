using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Components.NodeGraphTab.PropertyEditors;

/// <summary>Shows catalog choices of generative nodes as drop-downs.</summary>
public sealed class GenerativeChoicePropertyExtension : PropertyEditorExtension
{
    public static new readonly GenerativeChoicePropertyExtension Instance = new();

    // The choices are text inputs, and int ones for durations; a node shows the same drop-down as the panel.
    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        var options = new PropertyEditorOptions
        {
            Match = p => GenerativeChoices.TryGet(p, out _),
            CreateNodeControl = CreateControl,
        };
        registry.Register<string?, GenerativeChoiceEditorViewModel>(CreateContext, CreateControl, options);
        registry.Register<int, GenerativeChoiceEditorViewModel>(CreateContext, CreateControl, options);
    }

    private GenerativeChoiceEditorViewModel CreateContext(IPropertyAdapter property)
    {
        GenerativeChoices.TryGet(property, out GenerativeChoice? choice);
        return new GenerativeChoiceEditorViewModel(property, choice!, this);
    }

    private static Control CreateControl(IPropertyEditorContext context)
    {
        var editor = new EnumEditor();
        context.Accept(editor);
        return editor;
    }
}
