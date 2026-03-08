using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SceneEditorViewModel : ValueEditorViewModel<Scene?>
{
    public SceneEditorViewModel(IPropertyAdapter<Scene?> property)
        : base(property)
    {
        CurrentTargetName = Value
            .Select(scene => scene != null ? scene.Name : Message.Property_is_unset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public void SetTarget(Scene? target)
    {
        if (target == null)
        {
            SetNull();
            return;
        }

        if (PropertyAdapter is IExpressionPropertyAdapter<Scene?> exp)
        {
            exp.Expression = Expression.CreateReference<Scene>(target.Id);

            Commit();
        }
    }

    public void SetNull()
    {
        if (PropertyAdapter is IExpressionPropertyAdapter<Scene?> exp)
        {
            exp.Expression = null;
        }

        PropertyAdapter.SetValue(null);
        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableScenes()
    {
        var currentScene = this.GetService<EditViewModel>()?.Scene;
        if (currentScene == null) return [];

        var project = currentScene.FindHierarchicalParent<Project>();
        if (project == null) return [];

        return [.. project.Items
            .OfType<Scene>()
            .Where(s => s != currentScene)
            .Select(s => new TargetObjectInfo(s.Name, s, null))];
    }
}
