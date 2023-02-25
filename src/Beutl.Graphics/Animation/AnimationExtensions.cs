namespace Beutl.Animation;

public static class AnimationExtensions
{
    // Obsolete
    public static TimeSpan CalculateDuration(this IAnimation animation)
    {
        return animation.Duration;
    }
}
