using System.Runtime.InteropServices;

using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

// These two exports are supplied by native/SkiaSharp/vulkan-image-layout.patch.
// The handle shares its mutable state with every surface made from it.
internal static class SkiaVulkanInterop
{
    public static void EnsureAvailable()
    {
        try
        {
            // Both exports ignore a null texture, so these calls resolve them without allocating one.
            GetImageLayout(nint.Zero, out _);
            SetImageLayout(nint.Zero, (uint)ImageLayout.Undefined);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new NotSupportedException(
                "Vulkan rendering requires Beutl's libSkiaSharp built by native/SkiaSharp/build.py.", ex);
        }
    }

    public static ImageLayout GetImageLayout(GRBackendTexture texture)
    {
        bool success = GetImageLayout(texture.Handle, out uint layout);
        GC.KeepAlive(texture);
        if (!success)
            throw new InvalidOperationException("The Skia texture has no Vulkan image state.");
        return (ImageLayout)layout;
    }

    public static void SetImageLayout(GRBackendTexture texture, ImageLayout layout)
    {
        SetImageLayout(texture.Handle, (uint)layout);
        GC.KeepAlive(texture);
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "gr_beutl_backendtexture_get_vk_image_layout")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetImageLayout(nint texture, out uint layout);

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "gr_beutl_backendtexture_set_vk_image_layout")]
    private static extern void SetImageLayout(nint texture, uint layout);
}
