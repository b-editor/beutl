using System.Numerics;

namespace Beutl.Graphics3D;

internal static class Object3DTraversal
{
    /// <summary>
    /// Calls <paramref name="visit"/> with each enabled object under <paramref name="roots"/> and its world matrix,
    /// children before their parent. A disabled object hides its whole subtree.
    /// </summary>
    public static void ForEachEnabled(
        IReadOnlyList<Object3D.Resource> roots, Action<Object3D.Resource, Matrix4x4> visit)
    {
        foreach (Object3D.Resource root in roots)
        {
            Visit(root, Matrix4x4.Identity, visit);
        }
    }

    private static void Visit(Object3D.Resource obj, Matrix4x4 parentMatrix, Action<Object3D.Resource, Matrix4x4> visit)
    {
        if (!obj.IsEnabled)
            return;

        Matrix4x4 worldMatrix = obj.GetWorldMatrix() * parentMatrix;
        foreach (Object3D.Resource child in obj.GetChildResources())
        {
            Visit(child, worldMatrix, visit);
        }

        visit(obj, worldMatrix);
    }
}
