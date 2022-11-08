using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.Media;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AlignmentsPropertyEditorExtension : PropertyEditorExtension
{
    public static new readonly AlignmentsPropertyEditorExtension Instance = new();

    public override IEnumerable<CoreProperty> MatchProperty(IReadOnlyList<CoreProperty> properties)
    {
        bool foundX = false, foundY = false;

        for (int i = 0; i < properties.Count; i++)
        {
            CoreProperty item = properties[i];
            if (!foundX && item.PropertyType == typeof(AlignmentX))
            {
                yield return item;
                foundX = true;
            }

            if (!foundY && item.PropertyType == typeof(AlignmentY))
            {
                yield return item;
                foundY = true;
            }
        }
    }

    public override bool TryCreateContext(IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        IAbstractProperty<AlignmentX>? xProperty = null;
        IAbstractProperty<AlignmentY>? yProperty = null;

        for (int i = 0; i < properties.Count; i++)
        {
            IAbstractProperty item = properties[i];
            if (xProperty == null && item is IAbstractProperty<AlignmentX> xp)
            {
                xProperty = xp;
            }

            if (yProperty == null && item is IAbstractProperty<AlignmentY> yp)
            {
                yProperty = yp;
            }
        }

        if (xProperty != null && yProperty != null)
        {
            context = new AlignmentsEditorViewModel(xProperty, yProperty);
        }
        else
        {
            context = null;
        }

        return context != null;
    }

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out IControl? control)
    {
        if (context is AlignmentsEditorViewModel)
        {
            control = new AlignmentsEditor();
        }
        else
        {
            control = null;
        }

        return control != null;
    }
}
