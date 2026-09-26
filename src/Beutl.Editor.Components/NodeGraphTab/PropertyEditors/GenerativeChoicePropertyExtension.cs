using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.NodeGraph.Generative;

namespace Beutl.Editor.Components.NodeGraphTab.PropertyEditors;

/// <summary>Shows catalog choices of generative nodes as drop-downs.</summary>
[PrimitiveImpl]
public sealed class GenerativeChoicePropertyExtension : PropertyEditorExtension
{
    public static new readonly GenerativeChoicePropertyExtension Instance = new();

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        foreach (IPropertyAdapter property in properties)
        {
            if (property is IPropertyAdapter<string> && GenerativeChoices.TryGet(property, out _))
                return [property];
        }

        return [];
    }

    public override bool TryCreateContext(
        IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        context = null;
        if (properties.Count == 1
            && properties[0] is IPropertyAdapter<string> property
            && GenerativeChoices.TryGet(property, out GenerativeChoice? choice))
        {
            context = new GenerativeChoiceEditorViewModel(property, choice, this);
        }

        return context is not null;
    }

    public override bool TryCreateContextForNode(
        IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
        => TryCreateContext(properties, out context);

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        if (context is GenerativeChoiceEditorViewModel)
        {
            var editor = new EnumEditor();
            context.Accept(editor);
            control = editor;
            return true;
        }

        return base.TryCreateControl(context, out control);
    }

    public override bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        => TryCreateControl(context, out control);
}
