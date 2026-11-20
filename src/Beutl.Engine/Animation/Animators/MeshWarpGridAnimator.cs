using Beutl.Graphics;

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

        int rows = Math.Max(oldValue.Rows, newValue.Rows);
        int columns = Math.Max(oldValue.Columns, newValue.Columns);
        MeshWarpGrid from = oldValue.Resample(rows, columns);
        MeshWarpGrid to = newValue.Resample(rows, columns);
        if (from.Equals(to)) return from;

        return new MeshWarpGrid(rows, columns,
            from.Points.Select((point, index) => MeshWarpGrid.Lerp(point, to.Points[index], progress)));
    }
}
