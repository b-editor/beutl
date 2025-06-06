using System.Numerics;

namespace Beutl.Animation.Animators;

/// <summary>
/// Generic animator for numeric types that implement IFloatingPointIeee754&lt;T&gt;.
/// This replaces the need for individual animators for each numeric type.
/// </summary>
/// <typeparam name="T">The numeric type to animate</typeparam>
public class NumericAnimator<T> : Animator<T>
    where T : IFloatingPointIeee754<T>
{
    public override T Interpolate(float progress, T oldValue, T newValue)
    {
        return T.Lerp(oldValue, newValue, T.CreateChecked(progress));
    }
}
