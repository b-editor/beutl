using Beutl.Controls.PropertyEditors;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Components.NodeGraphTab.PropertyEditors;

/// <summary>Shows catalog choices of generative nodes as drop-downs.</summary>
public sealed class GenerativeChoicePropertyExtension : PropertyEditorExtension
{
    public static new readonly GenerativeChoicePropertyExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.RegisterGroup(
            properties => properties.FirstOrDefault(p => GenerativeChoices.TryGet(p, out _)) is { } property ? [property] : [],
            properties =>
            {
                GenerativeChoices.TryGet(properties[0], out GenerativeChoice? choice);
                return new GenerativeChoiceEditorViewModel(properties[0], choice!, this);
            },
            context =>
            {
                var editor = new EnumEditor();
                context.Accept(editor);
                return editor;
            });
    }
}
