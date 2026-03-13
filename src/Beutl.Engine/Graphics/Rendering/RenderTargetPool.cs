using Beutl.Graphics.Backend;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>RenderThread 専用（lock 不要）。</summary>
internal static class RenderTargetPool
{
    private readonly record struct PoolEntry(ITexture2D Texture, SKSurface Surface);
    private static readonly Dictionary<(int, int), Stack<PoolEntry>> s_pool = new();
    private static int s_totalPooled;
    private const int MaxPoolSize = 64;

    public static bool TryRent(int width, int height,
        out ITexture2D texture, out SKSurface surface)
    {
        if (s_pool.TryGetValue((width, height), out var stack) && stack.TryPop(out var entry))
        {
            s_totalPooled--;
            texture = entry.Texture;
            surface = entry.Surface;
            return true;
        }
        texture = null!;
        surface = null!;
        return false;
    }

    /// <summary>呼び出し元が GPU sync 済みであることを保証すること。</summary>
    public static bool TryReturn(int width, int height, ITexture2D texture, SKSurface surface)
    {
        if (s_totalPooled >= MaxPoolSize) return false;
        if (!s_pool.TryGetValue((width, height), out var stack))
            s_pool[(width, height)] = stack = new Stack<PoolEntry>();
        stack.Push(new PoolEntry(texture, surface));
        s_totalPooled++;
        return true;
    }

    /// <summary>VulkanContext.Dispose() の WaitIdle 後に呼ぶ。</summary>
    public static void Clear()
    {
        foreach (var stack in s_pool.Values)
            foreach (var entry in stack)
            {
                entry.Surface.Dispose();
                entry.Texture.Dispose();
            }
        s_pool.Clear();
        s_totalPooled = 0;
    }
}
