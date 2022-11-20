namespace Beutl.Animation;

public static class AnimationExtensions
{
    public static TimeSpan CalculateDuration(this IAnimation animation)
    {
        TimeSpan ts = TimeSpan.Zero;
        var children = animation.Children;
        for (int i = 0; i < children.Count; i++)
        {
            ts += children[i].Duration;
        }

        return ts;
    }

    public static TimeSpan CalculateDuration<T>(this Animation<T> animation)
    {
        TimeSpan ts = TimeSpan.Zero;
        foreach (AnimationSpan<T> item in animation.Children.GetMarshal().Value)
        {
            ts += item.Duration;
        }

        return ts;
    }
}
