using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class MeshWarpGridAnimator : Animator<MeshWarpGrid>
{
    public override MeshWarpGrid DefaultValue() => MeshWarpGrid.Default;

    public override MeshWarpGrid Interpolate(float progress, MeshWarpGrid? oldValue, MeshWarpGrid? newValue)
    {
        if (oldValue is null) return newValue ?? DefaultValue();
        if (newValue is null) return oldValue;
        if (progress == 1) return newValue;

        oldValue = oldValue.Resample(newValue.Rows, newValue.Columns);
        if (progress == 0 || !float.IsFinite(progress) || oldValue.Equals(newValue))
            return oldValue;

        return new MeshWarpGrid(newValue.Rows, newValue.Columns,
            oldValue.Points.Select((point, index) => MeshWarpGrid.Lerp(point, newValue.Points[index], progress)));
    }
}
