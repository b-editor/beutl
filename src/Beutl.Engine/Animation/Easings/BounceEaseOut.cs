using System.Runtime.CompilerServices;

namespace Beutl.Animation.Easings;

public sealed class BounceEaseOut : Easing
{
    public override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = -0.000001f;
        maximum = 1.000001f;
        return true;
    }

    public override float Ease(float progress)
    {
        return Evaluate(progress);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Evaluate(float p)
    {
        if (p < 4f / 11.0f)
        {
            return (121f * p * p) / 16.0f;
        }
        else if (p < 8f / 11.0f)
        {
            return (363f / 40.0f * p * p) - (99f / 10.0f * p) + 17f / 5.0f;
        }
        else if (p < 9f / 10.0f)
        {
            return (4356f / 361.0f * p * p) - (35442f / 1805.0f * p) + 16061f / 1805.0f;
        }
        else
        {
            return (54f / 5.0f * p * p) - (513f / 25.0f * p) + 268f / 25.0f;
        }
    }
}
