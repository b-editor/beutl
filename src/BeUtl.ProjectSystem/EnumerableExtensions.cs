namespace Beutl;

internal static class EnumerableExtensions
{
    public static int Nearest(this IEnumerable<int> list, int value)
    {
        return list.Aggregate((x, y) => Math.Abs(x - value) < Math.Abs(y - value) ? x : y);
    }
}
