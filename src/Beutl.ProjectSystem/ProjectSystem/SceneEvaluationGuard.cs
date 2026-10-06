namespace Beutl.ProjectSystem;

// The scenes being evaluated on the current async flow, so a scene that contains itself is reported
// instead of recursing.
internal sealed class SceneEvaluationGuard
{
    private readonly AsyncLocal<HashSet<Scene>?> _evaluatingScenes = new();

    public bool Enter(Scene scene)
    {
        var set = _evaluatingScenes.Value ??= new(ReferenceEqualityComparer.Instance);
        return set.Add(scene);
    }

    public void Exit(Scene scene)
    {
        _evaluatingScenes.Value?.Remove(scene);
    }
}
