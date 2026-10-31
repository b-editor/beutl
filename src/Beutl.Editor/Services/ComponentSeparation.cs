using Beutl.Animation;

namespace Beutl.Editor.Services;

// Whether a property's components can be animated separately, as the property editors and the graph editor offer.
internal static class ComponentSeparation
{
    public static bool CanSeparate(IProperty? property)
        => property is IExpressionProperty { IsAnimatable: true } && ComponentAnimations.IsSupported(property.ValueType);
}
