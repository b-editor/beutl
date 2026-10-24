using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.TestInfrastructure;

internal static class SceneReferenceTestHelper
{
    public static ReferenceExpression<Scene?> Create(Scene owner, Scene target)
    {
        Project project = owner.FindHierarchicalParent<Project>() ?? new Project { Items = { owner } };
        if (project.FindHierarchicalParent<BeutlApplication>() == null)
            _ = new BeutlApplication { Project = { Value = project } };
        if (!project.Items.Contains(target))
            project.Items.Add(target);
        return new ReferenceExpression<Scene?>(target.Id.Value);
    }
}
