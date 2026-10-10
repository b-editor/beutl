using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Editors;

namespace Beutl.Services.Adapters;

internal sealed class PropertiesEditorFactoryImpl(ExtensionProvider extensionProvider) : IPropertiesEditorFactory
{
    public IPropertiesEditorViewModel Create(ICoreObject obj)
    {
        // A transition is shown through the element property that holds it, alone, so its type can be
        // changed alongside its own properties.
        if (obj is ClipTransition { HierarchicalParent: Element element } transition
            && GetHoldingProperty(element, transition) is { } property)
        {
            return new PropertiesEditorViewModel(transition, element, extensionProvider, (p, _) => p.Id == property.Id);
        }

        return new PropertiesEditorViewModel(obj, extensionProvider);
    }

    private static CoreProperty? GetHoldingProperty(Element element, ClipTransition transition)
    {
        if (ReferenceEquals(element.EnterTransition, transition)) return Element.EnterTransitionProperty;
        if (ReferenceEquals(element.ExitTransition, transition)) return Element.ExitTransitionProperty;
        return null;
    }
}
