using System.Numerics;

namespace Beutl.Graphics.Effects;

internal sealed partial class PuppetMesh(Vector2[] rest, ushort[] indices, int[] pinVertices)
{
    internal const int BoundaryVertexBudget = 8192;

    public Vector2[] Rest { get; } = rest;
    public ushort[] Indices { get; } = indices;
    public int[] PinVertices { get; } = pinVertices;

    public bool TryMapToRest(ReadOnlySpan<Vector2> deformed, Vector2 point, out Vector2 restPoint)
    {
        // If the user folds the mesh over itself, look at the triangles in reverse draw order.
        for (int i = Indices.Length - 3; i >= 0; i -= 3)
        {
            int a = Indices[i];
            int b = Indices[i + 1];
            int c = Indices[i + 2];
            Vector2 u = deformed[b] - deformed[a];
            Vector2 v = deformed[c] - deformed[a];
            Vector2 offset = point - deformed[a];
            double area = Cross(u, v);
            if (area == 0) continue;

            double wb = Cross(offset, v) / area;
            double wc = Cross(u, offset) / area;
            double wa = 1 - wb - wc;
            const double edgeTolerance = 1e-6;
            if (wa >= -edgeTolerance && wb >= -edgeTolerance && wc >= -edgeTolerance)
            {
                // A hit just outside a triangle is projected back onto its edge before the anchor is stored.
                wa = Math.Max(0, wa);
                wb = Math.Max(0, wb);
                wc = Math.Max(0, wc);
                double sum = wa + wb + wc;
                restPoint = new Vector2(
                    (float)((wa * Rest[a].X + wb * Rest[b].X + wc * Rest[c].X) / sum),
                    (float)((wa * Rest[a].Y + wb * Rest[b].Y + wc * Rest[c].Y) / sum));
                return true;
            }
        }

        restPoint = default;
        return false;
    }

    internal static Vector2 ToLocal(Size size, Point normalized) => new(normalized.X * size.Width, normalized.Y * size.Height);

    internal static Point Normalize(Size size, Vector2 point) => new(point.X / size.Width, point.Y / size.Height);

    private static double Cross(Vector2 left, Vector2 right) => (double)left.X * right.Y - (double)left.Y * right.X;
}
