using Beutl.Engine.Expressions;

namespace Beutl.ProjectSystem;

// How SceneDrawable and SceneSound read the scene their ReferencedScene names. The editor stores a reference and leaves
// the value null, and playback reads the reference over any value that is stored.
internal static class SceneReference
{
    // A plain reference names one scene for the whole clip; any other expression can pick another as it plays.
    public static bool IsDynamic(IExpressionProperty<Scene?> property)
        => property.Expression is not (null or IReferenceExpression { HasPropertyPath: false });

    // Null when no scene is named or a reference finds none, which plays nothing. A referenced scene is an item of the
    // project, so this needs no walk of the object graph, which edits would repeat for every gesture.
    public static MediaTime? GetDuration(IHierarchical owner, IExpressionProperty<Scene?> property)
    {
        Scene? scene = property.Expression switch
        {
            null => property.Value,
            IReferenceExpression { HasPropertyPath: false } reference => owner.FindHierarchicalParent<Project>()?.Items
                .FirstOrDefault(item => item.Id.Value == reference.ObjectId) as Scene,
            _ => null
        };
        return scene?.Duration.Value;
    }
}
