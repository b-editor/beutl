using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.Animation.Animators;

public sealed class MeshWarpGridAnimator : Animator<MeshWarpGrid>
{
    public override MeshWarpGrid DefaultValue() => MeshWarpGrid.Default;

    public override MeshWarpGrid Interpolate(float progress, MeshWarpGrid? oldValue, MeshWarpGrid? newValue)
    {
        if (oldValue is null) return newValue ?? DefaultValue();
        if (newValue is null) return oldValue;
        if (progress == 0 || !float.IsFinite(progress)) return oldValue;
        if (progress == 1) return newValue;

        int rows = CommonCells(oldValue.Rows, newValue.Rows);
        int columns = CommonCells(oldValue.Columns, newValue.Columns);
        return InterpolateResampled(progress, oldValue, newValue, rows, columns, MeshWarpInterpolation.Linear);
    }

    internal static MeshWarpGrid Interpolate(
        float progress,
        MeshWarpGrid? oldValue,
        MeshWarpGrid? newValue,
        int targetRows,
        int targetColumns,
        MeshWarpInterpolation interpolation)
    {
        if (oldValue is null)
            return newValue?.Resample(targetRows, targetColumns, interpolation) ?? MeshWarpGrid.CreateIdentity(targetRows, targetColumns);
        if (newValue is null)
            return oldValue.Resample(targetRows, targetColumns, interpolation);
        if (progress == 0 || !float.IsFinite(progress))
            return oldValue.Resample(targetRows, targetColumns, interpolation);
        if (progress == 1)
            return newValue.Resample(targetRows, targetColumns, interpolation);

        return InterpolateResampled(progress, oldValue, newValue, targetRows, targetColumns, interpolation);
    }

    private static MeshWarpGrid InterpolateResampled(
        float progress,
        MeshWarpGrid oldValue,
        MeshWarpGrid newValue,
        int rows,
        int columns,
        MeshWarpInterpolation interpolation)
    {
        MeshWarpGrid from = oldValue.Resample(rows, columns, interpolation);
        MeshWarpGrid to = newValue.Resample(rows, columns, interpolation);
        if (from.Equals(to)) return from;

        return new MeshWarpGrid(from.Rows, from.Columns,
            from.Points.Select((point, index) => MeshWarpGrid.Lerp(point, to.Points[index], progress)));
    }

    private static int CommonCells(int a, int b)
    {
        if (a == b) return a;
        int lcm = a / Gcd(a, b) * b;
        return lcm <= MeshWarpGrid.MaxCells ? lcm : Math.Max(a, b);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
