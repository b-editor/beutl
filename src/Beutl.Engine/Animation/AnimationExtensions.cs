using System.Diagnostics.CodeAnalysis;

namespace Beutl.Animation;

public static class AnimationExtensions
{
    // Obsolete
    [Obsolete("Use Duration property instead")]
    [ExcludeFromCodeCoverage]
    public static TimeSpan CalculateDuration(this IAnimation animation)
    {
        return animation.Duration;
    }
}
