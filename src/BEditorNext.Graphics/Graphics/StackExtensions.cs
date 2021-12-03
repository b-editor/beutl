namespace BEditorNext.Graphics;

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
}
