using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D;

/// <summary>
/// Represents a ray for 3D hit testing.
/// </summary>
public readonly struct Ray3D
{
    public Ray3D(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    /// <summary>
    /// The origin point of the ray.
    /// </summary>
    public Vector3 Origin { get; }

    /// <summary>
    /// The normalized direction of the ray.
    /// </summary>
    public Vector3 Direction { get; }

    /// <summary>
    /// Gets a point along the ray at the specified distance.
    /// </summary>
    public Vector3 GetPoint(float distance) => Origin + Direction * distance;
}

/// <summary>
/// Provides 3D hit testing functionality using ray casting.
/// </summary>
public static class HitTester3D
{
    /// <summary>
    /// Performs a hit test at the specified screen point (recursively searches children).
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="objects">The list of 3D objects to test against.</param>
    /// <returns>The closest 3D object at that point, or null if none.</returns>
    public static Object3D.Resource? HitTest(
        Point screenPoint,
        int width,
        int height,
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects)
    {
        if (objects.Count == 0)
            return null;

        // Create ray from screen point
        if (!TryCreateRayFromScreen(screenPoint, width, height, camera, out var ray))
            return null;

        // Test intersection with each object recursively
        Object3D.Resource? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (var obj in objects)
        {
            if (!obj.IsEnabled)
                continue;

            // Recursively hit test this object and its children
            var (hitObj, distance) = HitTestRecursive(ray, obj, Matrix4x4.Identity);
            if (hitObj != null && distance < closestDistance)
            {
                closestDistance = distance;
                closestObject = hitObj;
            }
        }

        return closestObject;
    }

    /// <summary>
    /// Recursively performs hit testing on an object and its children.
    /// </summary>
    private static (Object3D.Resource? obj, float distance) HitTestRecursive(
        Ray3D ray,
        Object3D.Resource obj,
        Matrix4x4 parentMatrix)
    {
        if (!obj.IsEnabled)
            return (null, float.MaxValue);

        Object3D.Resource? closestObject = null;
        float closestDistance = float.MaxValue;

        // Combine with parent matrix
        var worldMatrix = obj.GetWorldMatrix() * parentMatrix;

        // Recursively test children
        var children = obj.GetChildResources();
        foreach (var child in children)
        {
            var (hitObj, distance) = HitTestRecursive(ray, child, worldMatrix);
            if (hitObj != null && distance < closestDistance)
            {
                closestDistance = distance;
                closestObject = hitObj;
            }
        }

        // Test this object's mesh
        var mesh = obj.GetMesh();
        if (mesh != null)
        {
            if (Matrix4x4.Invert(worldMatrix, out var invWorld))
            {
                var localRay = TransformRay(ray, invWorld);
                var bbox = mesh.GetBoundingBox();

                if (RayIntersectsBoundingBox(localRay, bbox, out float bboxDistance))
                {
                    if (RayIntersectsMesh(localRay, mesh, out float meshDistance))
                    {
                        // Use mesh distance for more accurate sorting
                        if (meshDistance < closestDistance)
                        {
                            closestDistance = meshDistance;
                            closestObject = obj;
                        }
                    }
                }
            }
        }

        return (closestObject, closestDistance);
    }

    /// <summary>
    /// Performs a hit test and returns the path from root to the hit object.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="objects">The list of 3D objects to test against.</param>
    /// <returns>A list representing the path from root to the closest hit object, or empty if none.</returns>
    public static IReadOnlyList<Object3D.Resource> HitTestWithPath(
        Point screenPoint,
        int width,
        int height,
        Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects)
    {
        if (objects.Count == 0)
            return [];

        // Create ray from screen point
        if (!TryCreateRayFromScreen(screenPoint, width, height, camera, out var ray))
            return [];

        // Test intersection with each object recursively
        List<Object3D.Resource>? closestPath = null;
        float closestDistance = float.MaxValue;

        foreach (var obj in objects)
        {
            if (!obj.IsEnabled)
                continue;

            // Recursively hit test this object and its children, collecting the path
            var currentPath = new List<Object3D.Resource>();
            var (hitPath, distance) = HitTestRecursiveWithPath(ray, obj, Matrix4x4.Identity, currentPath);
            if (hitPath != null && distance < closestDistance)
            {
                closestDistance = distance;
                closestPath = hitPath;
            }
        }

        return closestPath ?? [];
    }

    /// <summary>
    /// Recursively performs hit testing and collects the path to the hit object.
    /// </summary>
    private static (List<Object3D.Resource>? path, float distance) HitTestRecursiveWithPath(
        Ray3D ray,
        Object3D.Resource obj,
        Matrix4x4 parentMatrix,
        List<Object3D.Resource> currentPath)
    {
        if (!obj.IsEnabled)
            return (null, float.MaxValue);

        // Add this object to the current path
        currentPath.Add(obj);

        List<Object3D.Resource>? closestPath = null;
        float closestDistance = float.MaxValue;

        // Combine with parent matrix
        var worldMatrix = obj.GetWorldMatrix() * parentMatrix;

        // Recursively test children
        var children = obj.GetChildResources();
        foreach (var child in children)
        {
            // Create a copy of the current path for each child branch
            var childPath = new List<Object3D.Resource>(currentPath);
            var (hitPath, distance) = HitTestRecursiveWithPath(ray, child, worldMatrix, childPath);
            if (hitPath != null && distance < closestDistance)
            {
                closestDistance = distance;
                closestPath = hitPath;
            }
        }

        // Test this object's mesh
        var mesh = obj.GetMesh();
        if (mesh != null)
        {
            if (Matrix4x4.Invert(worldMatrix, out var invWorld))
            {
                var localRay = TransformRay(ray, invWorld);
                var bbox = mesh.GetBoundingBox();

                if (RayIntersectsBoundingBox(localRay, bbox, out float bboxDistance))
                {
                    if (RayIntersectsMesh(localRay, mesh, out float meshDistance))
                    {
                        // If this mesh is closer than any child hits, use this path
                        if (meshDistance < closestDistance)
                        {
                            closestDistance = meshDistance;
                            closestPath = new List<Object3D.Resource>(currentPath);
                        }
                    }
                }
            }
        }

        return (closestPath, closestDistance);
    }

    /// <summary>
    /// Tries to create a ray from screen coordinates.
    /// </summary>
    /// <param name="screenPoint">The screen point.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="ray">The resulting ray in world space.</param>
    /// <returns>True if the ray was successfully created.</returns>
    public static bool TryCreateRayFromScreen(
        Point screenPoint,
        int width,
        int height,
        Camera3D.Resource camera,
        out Ray3D ray)
    {
        ray = default;

        // Convert screen coordinates to normalized device coordinates (-1 to 1)
        float ndcX = (2.0f * screenPoint.X / width) - 1.0f;
        float ndcY = 1.0f - (2.0f * screenPoint.Y / height);

        // Get camera matrices
        float aspectRatio = (float)width / height;
        var viewMatrix = camera.GetViewMatrix();
        var projMatrix = camera.GetProjectionMatrix(aspectRatio);

        // Invert the matrices to transform from screen space to world space
        if (!Matrix4x4.Invert(viewMatrix, out var invView))
            return false;
        if (!Matrix4x4.Invert(projMatrix, out var invProj))
            return false;

        // Calculate ray origin and direction
        var nearPoint = new Vector4(ndcX, ndcY, 0, 1);
        var farPoint = new Vector4(ndcX, ndcY, 1, 1);

        // Transform to view space
        nearPoint = Vector4.Transform(nearPoint, invProj);
        farPoint = Vector4.Transform(farPoint, invProj);

        // Perspective divide
        nearPoint /= nearPoint.W;
        farPoint /= farPoint.W;

        // Transform to world space
        nearPoint = Vector4.Transform(nearPoint, invView);
        farPoint = Vector4.Transform(farPoint, invView);

        var rayOrigin = new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z);
        var rayEnd = new Vector3(farPoint.X, farPoint.Y, farPoint.Z);
        var rayDirection = rayEnd - rayOrigin;

        ray = new Ray3D(rayOrigin, rayDirection);
        return true;
    }

    /// <summary>
    /// Transforms a ray by a matrix.
    /// </summary>
    /// <param name="ray">The ray to transform.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The transformed ray.</returns>
    public static Ray3D TransformRay(Ray3D ray, Matrix4x4 matrix)
    {
        var origin = Vector3.Transform(ray.Origin, matrix);
        var endPoint = Vector3.Transform(ray.GetPoint(1.0f), matrix);
        return new Ray3D(origin, endPoint - origin);
    }

    /// <summary>
    /// Tests if a ray intersects with an axis-aligned bounding box.
    /// </summary>
    /// <param name="ray">The ray to test.</param>
    /// <param name="bbox">The bounding box.</param>
    /// <param name="distance">The distance to the intersection point.</param>
    /// <returns>True if the ray intersects the bounding box.</returns>
    public static bool RayIntersectsBoundingBox(Ray3D ray, BoundingBox bbox, out float distance)
    {
        distance = 0;

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        // X axis
        if (Math.Abs(ray.Direction.X) > float.Epsilon)
        {
            float t1 = (bbox.Min.X - ray.Origin.X) / ray.Direction.X;
            float t2 = (bbox.Max.X - ray.Origin.X) / ray.Direction.X;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        else if (ray.Origin.X < bbox.Min.X || ray.Origin.X > bbox.Max.X)
        {
            return false;
        }

        // Y axis
        if (Math.Abs(ray.Direction.Y) > float.Epsilon)
        {
            float t1 = (bbox.Min.Y - ray.Origin.Y) / ray.Direction.Y;
            float t2 = (bbox.Max.Y - ray.Origin.Y) / ray.Direction.Y;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        else if (ray.Origin.Y < bbox.Min.Y || ray.Origin.Y > bbox.Max.Y)
        {
            return false;
        }

        // Z axis
        if (Math.Abs(ray.Direction.Z) > float.Epsilon)
        {
            float t1 = (bbox.Min.Z - ray.Origin.Z) / ray.Direction.Z;
            float t2 = (bbox.Max.Z - ray.Origin.Z) / ray.Direction.Z;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        else if (ray.Origin.Z < bbox.Min.Z || ray.Origin.Z > bbox.Max.Z)
        {
            return false;
        }

        distance = tMin >= 0 ? tMin : tMax;
        return distance >= 0;
    }

    /// <summary>
    /// Tests if a ray intersects with a mesh.
    /// </summary>
    /// <param name="ray">The ray to test.</param>
    /// <param name="mesh">The mesh to test against.</param>
    /// <param name="distance">The distance to the closest intersection point.</param>
    /// <returns>True if the ray intersects the mesh.</returns>
    public static bool RayIntersectsMesh(Ray3D ray, Mesh.Resource mesh, out float distance)
    {
        distance = float.MaxValue;
        bool hit = false;

        var vertices = mesh.GetVertices();
        var indices = mesh.GetIndices();

        // Iterate through triangles
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var v0 = vertices[(int)indices[i]].Position;
            var v1 = vertices[(int)indices[i + 1]].Position;
            var v2 = vertices[(int)indices[i + 2]].Position;

            if (RayIntersectsTriangle(ray, v0, v1, v2, out float t))
            {
                if (t > 0 && t < distance)
                {
                    distance = t;
                    hit = true;
                }
            }
        }

        return hit;
    }

    /// <summary>
    /// Tests if a ray intersects with a triangle using the Möller–Trumbore algorithm.
    /// </summary>
    /// <param name="ray">The ray to test.</param>
    /// <param name="v0">First vertex of the triangle.</param>
    /// <param name="v1">Second vertex of the triangle.</param>
    /// <param name="v2">Third vertex of the triangle.</param>
    /// <param name="t">The distance along the ray to the intersection point.</param>
    /// <returns>True if the ray intersects the triangle.</returns>
    public static bool RayIntersectsTriangle(Ray3D ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        const float epsilon = 1e-8f;
        t = 0;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;

        var h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -epsilon && a < epsilon)
            return false; // Ray is parallel to triangle

        float f = 1.0f / a;
        var s = ray.Origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0 || u > 1)
            return false;

        var q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.Direction, q);

        if (v < 0 || u + v > 1)
            return false;

        t = f * Vector3.Dot(edge2, q);
        return t > epsilon;
    }
}
