namespace BeUtl.Graphics;

internal static class StackExtensions
{
    public static T PeekOrDefault<T>(this Stack<T> stack, T defaultValue)
    {
        if (stack.TryPeek(out T? result))
        {
            return result;
        }
        else
        {
            return defaultValue;
        }
    }

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
