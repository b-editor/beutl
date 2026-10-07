namespace Beutl.Graphics;

internal static class StackExtensions
{
    public static T PopOrDefault<T>(this Stack<T> stack, T defaultValue)
    {
        if (stack.TryPop(out T? result))
        {
            return result;
        }
        else
        {
            return defaultValue;
        }
    }
}
