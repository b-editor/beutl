using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Gizmo;

/// <summary>
/// Provides hit testing for 3D gizmos.
/// </summary>
public static class GizmoHitTester
{
    // Gizmo dimensions (must match GizmoMesh)
    private const float ArrowLength = 1.0f;
    private const float ArrowRadius = 0.08f; // Larger than visual for easier clicking
    private const float RotateRingRadius = 0.8f;
    private const float RotateRingThickness = 0.08f; // Larger than visual for easier clicking
    private const float ScaleLineLength = 0.8f;
    private const float ScaleCubeSize = 0.12f; // Larger than visual for easier clicking

    // Plane dimensions for translate mode (must match GizmoMesh)
    private const float PlaneOffset = 0.0f;
    private const float PlaneSize = 0.2f;
    private const float PlaneHitPadding = 0.05f; // Extra padding for easier clicking

    // Center cube for uniform scale (must match GizmoMesh)
    private const float CenterCubeSize = 0.15f; // Slightly larger for easier clicking

    /// <summary>
    /// Performs a hit test on the gizmo at the specified screen point.
    /// </summary>
    /// <param name="screenPoint">The point in screen coordinates.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="gizmoPosition">The world position of the gizmo (target object's position).</param>
    /// <param name="gizmoRotation">The rotation of the object (Euler angles in degrees). Used for Scale mode.</param>
    /// <param name="gizmoMode">The current gizmo mode.</param>
    /// <returns>The axis that was hit, or None if no axis was hit.</returns>
    public static GizmoAxis HitTest(
        Point screenPoint,
        int width,
        int height,
        Camera3D.Resource camera,
        Vector3 gizmoPosition,
        Vector3 gizmoRotation,
        GizmoMode gizmoMode)
    {
        if (gizmoMode == GizmoMode.None)
            return GizmoAxis.None;

        // Create ray from screen point
        if (!HitTester3D.TryCreateRayFromScreen(screenPoint, width, height, camera, out var ray))
            return GizmoAxis.None;

        // Transform ray to gizmo local space
        Ray3D localRay;
        if (gizmoMode is GizmoMode.Rotate or GizmoMode.Scale)
        {
            // For Rotate and Scale modes, apply inverse rotation to transform ray into object's local space
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                gizmoRotation.Y * MathF.PI / 180f,
                gizmoRotation.X * MathF.PI / 180f,
                gizmoRotation.Z * MathF.PI / 180f);

            // Invert the rotation matrix
            Matrix4x4.Invert(rotationMatrix, out var inverseRotation);

            // Transform ray origin and direction by inverse rotation
            var localOrigin = Vector3.Transform(ray.Origin - gizmoPosition, inverseRotation);
            var localDirection = Vector3.TransformNormal(ray.Direction, inverseRotation);
            localRay = new Ray3D(localOrigin, Vector3.Normalize(localDirection));
        }
        else
        {
            // For Translate mode, gizmo is world-aligned
            localRay = new Ray3D(ray.Origin - gizmoPosition, ray.Direction);
        }

        GizmoAxis closestAxis = GizmoAxis.None;
        float closestDistance = float.MaxValue;

        // Test each axis
        GizmoAxis[] axes = [GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z];
        Vector3[] axisDirections = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

        for (int i = 0; i < 3; i++)
        {
            float distance = float.MaxValue;
            bool hit = gizmoMode switch
            {
                GizmoMode.Translate => RayIntersectsCylinder(localRay, Vector3.Zero, axisDirections[i], ArrowLength, ArrowRadius, out distance),
                GizmoMode.Rotate => RayIntersectsRing(localRay, axisDirections[i], RotateRingRadius, RotateRingThickness, out distance),
                GizmoMode.Scale => RayIntersectsScaleAxis(localRay, axisDirections[i], ScaleLineLength, ScaleCubeSize, out distance),
                _ => false
            };

            if (hit && distance < closestDistance)
            {
                closestDistance = distance;
                closestAxis = axes[i];
            }
        }

        // Test plane indicators for translate mode
        if (gizmoMode == GizmoMode.Translate)
        {
            // XY plane
            if (RayIntersectsPlaneQuad(localRay, Vector3.UnitX, Vector3.UnitY, out float xyDistance))
            {
                if (xyDistance < closestDistance)
                {
                    closestDistance = xyDistance;
                    closestAxis = GizmoAxis.XY;
                }
            }

            // YZ plane
            if (RayIntersectsPlaneQuad(localRay, Vector3.UnitY, Vector3.UnitZ, out float yzDistance))
            {
                if (yzDistance < closestDistance)
                {
                    closestDistance = yzDistance;
                    closestAxis = GizmoAxis.YZ;
                }
            }

            // ZX plane
            if (RayIntersectsPlaneQuad(localRay, Vector3.UnitZ, Vector3.UnitX, out float zxDistance))
            {
                if (zxDistance < closestDistance)
                {
                    closestDistance = zxDistance;
                    closestAxis = GizmoAxis.ZX;
                }
            }
        }

        // Test center cube for uniform scale mode
        if (gizmoMode == GizmoMode.Scale)
        {
            if (RayIntersectsBox(localRay, Vector3.Zero, CenterCubeSize, out float centerDistance))
            {
                if (centerDistance < closestDistance)
                {
                    closestDistance = centerDistance;
                    closestAxis = GizmoAxis.All;
                }
            }
        }

        return closestAxis;
    }

    /// <summary>
    /// Tests if a ray intersects with a cylinder along an axis.
    /// </summary>
    private static bool RayIntersectsCylinder(Ray3D ray, Vector3 origin, Vector3 direction, float length, float radius, out float distance)
    {
        distance = float.MaxValue;

        // Project the problem to 2D by removing the axis component
        // For simplicity, use a capsule approximation (cylinder with spherical caps)

        // Calculate the closest point on the cylinder axis to the ray
        var w = ray.Origin - origin;

        float a = Vector3.Dot(ray.Direction, ray.Direction) - MathF.Pow(Vector3.Dot(ray.Direction, direction), 2);
        float b = 2 * (Vector3.Dot(ray.Direction, w) - Vector3.Dot(ray.Direction, direction) * Vector3.Dot(w, direction));
        float c = Vector3.Dot(w, w) - MathF.Pow(Vector3.Dot(w, direction), 2) - radius * radius;

        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0)
            return false;

        float t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
        if (t < 0)
        {
            t = (-b + MathF.Sqrt(discriminant)) / (2 * a);
            if (t < 0)
                return false;
        }

        // Check if the hit point is within the cylinder's length
        var hitPoint = ray.GetPoint(t);
        float axisProjection = Vector3.Dot(hitPoint - origin, direction);

        if (axisProjection >= 0 && axisProjection <= length)
        {
            distance = t;
            return true;
        }

        // Check sphere at the end
        var endPoint = origin + direction * length;
        if (RayIntersectsSphere(ray, endPoint, radius * 1.5f, out float sphereDistance))
        {
            distance = sphereDistance;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tests if a ray intersects with a ring (torus).
    /// </summary>
    private static bool RayIntersectsRing(Ray3D ray, Vector3 axis, float majorRadius, float minorRadius, out float distance)
    {
        distance = float.MaxValue;

        // Simplified ring hit test: check intersection with a flat annulus (disc with hole)
        // Then check if the hit point is within the torus tube radius

        // Find intersection with the plane perpendicular to the axis
        float denom = Vector3.Dot(ray.Direction, axis);

        // If ray is nearly parallel to the plane, check from the side
        if (MathF.Abs(denom) < 0.001f)
        {
            // Side approach - check cylindrical intersection at major radius
            return RayIntersectsRingSide(ray, axis, majorRadius, minorRadius, out distance);
        }

        float t = -Vector3.Dot(ray.Origin, axis) / denom;
        if (t < 0)
            return false;

        var hitPoint = ray.GetPoint(t);

        // Remove axis component
        var flatPoint = hitPoint - axis * Vector3.Dot(hitPoint, axis);
        float distFromCenter = flatPoint.Length();

        // Check if within the torus (between inner and outer radius)
        float innerRadius = majorRadius - minorRadius;
        float outerRadius = majorRadius + minorRadius;

        if (distFromCenter >= innerRadius && distFromCenter <= outerRadius)
        {
            distance = t;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tests ring intersection from the side.
    /// </summary>
    private static bool RayIntersectsRingSide(Ray3D ray, Vector3 axis, float majorRadius, float minorRadius, out float distance)
    {
        distance = float.MaxValue;

        // Calculate perpendicular directions
        var up = MathF.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var tangent1 = Vector3.Normalize(Vector3.Cross(axis, up));
        var tangent2 = Vector3.Normalize(Vector3.Cross(axis, tangent1));

        // Sample points around the ring and test sphere intersections
        const int samples = 8;
        for (int i = 0; i < samples; i++)
        {
            float angle = i * MathF.PI * 2 / samples;
            var ringPoint = tangent1 * MathF.Cos(angle) * majorRadius + tangent2 * MathF.Sin(angle) * majorRadius;

            if (RayIntersectsSphere(ray, ringPoint, minorRadius, out float d))
            {
                if (d < distance)
                    distance = d;
            }
        }

        return distance < float.MaxValue;
    }

    /// <summary>
    /// Tests if a ray intersects with a scale axis (line with cube at end).
    /// </summary>
    private static bool RayIntersectsScaleAxis(Ray3D ray, Vector3 direction, float length, float cubeSize, out float distance)
    {
        distance = float.MaxValue;
        bool hit = false;

        // Check the line (thin cylinder)
        if (RayIntersectsCylinder(ray, Vector3.Zero, direction, length, 0.05f, out float lineDistance))
        {
            distance = lineDistance;
            hit = true;
        }

        // Check the cube at the end
        var cubeCenter = direction * length;
        if (RayIntersectsBox(ray, cubeCenter, cubeSize, out float cubeDistance))
        {
            if (cubeDistance < distance)
            {
                distance = cubeDistance;
                hit = true;
            }
        }

        return hit;
    }

    /// <summary>
    /// Tests if a ray intersects with a sphere.
    /// </summary>
    private static bool RayIntersectsSphere(Ray3D ray, Vector3 center, float radius, out float distance)
    {
        distance = float.MaxValue;

        var oc = ray.Origin - center;
        float a = Vector3.Dot(ray.Direction, ray.Direction);
        float b = 2.0f * Vector3.Dot(oc, ray.Direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;

        float discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
            return false;

        float t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
        if (t < 0)
        {
            t = (-b + MathF.Sqrt(discriminant)) / (2 * a);
            if (t < 0)
                return false;
        }

        distance = t;
        return true;
    }

    /// <summary>
    /// Tests if a ray intersects with an axis-aligned box.
    /// </summary>
    private static bool RayIntersectsBox(Ray3D ray, Vector3 center, float size, out float distance)
    {
        float half = size * 0.5f;
        var min = center - new Vector3(half, half, half);
        var max = center + new Vector3(half, half, half);

        var bbox = new BoundingBox(min, max);
        return HitTester3D.RayIntersectsBoundingBox(ray, bbox, out distance);
    }

    /// <summary>
    /// Tests if a ray intersects with a plane quad defined by two axes.
    /// The quad is positioned at (PlaneOffset, PlaneOffset) in the plane.
    /// </summary>
    private static bool RayIntersectsPlaneQuad(Ray3D ray, Vector3 axis1, Vector3 axis2, out float distance)
    {
        distance = float.MaxValue;

        // Calculate the normal of the plane (perpendicular to both axes)
        var normal = Vector3.Cross(axis1, axis2);
        if (normal.LengthSquared() < 0.0001f)
            return false;
        normal = Vector3.Normalize(normal);

        // Find intersection with the plane
        float denom = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 0.0001f)
            return false; // Ray is parallel to plane

        // The plane passes through the center of the quad
        var quadCenter = axis1 * (PlaneOffset + PlaneSize * 0.5f) + axis2 * (PlaneOffset + PlaneSize * 0.5f);
        float t = Vector3.Dot(quadCenter - ray.Origin, normal) / denom;

        if (t < 0)
            return false;

        var hitPoint = ray.GetPoint(t);

        // Check if hit point is within the quad bounds (with padding)
        float proj1 = Vector3.Dot(hitPoint, axis1);
        float proj2 = Vector3.Dot(hitPoint, axis2);

        float minBound = PlaneOffset - PlaneHitPadding;
        float maxBound = PlaneOffset + PlaneSize + PlaneHitPadding;

        if (proj1 >= minBound && proj1 <= maxBound &&
            proj2 >= minBound && proj2 <= maxBound)
        {
            distance = t;
            return true;
        }

        return false;
    }
}
