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
            // The pinned Skia SetVkImageLayout checks for null before accessing the target.
            // These no-op calls resolve both exports without allocating a render target.
            GetImageLayout(nint.Zero, out _);
            SetImageLayout(nint.Zero, (uint)ImageLayout.Undefined);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new NotSupportedException(
                "Vulkan rendering requires Beutl's libSkiaSharp built by native/SkiaSharp/build.py.", ex);
        }
    }

    public static ImageLayout GetImageLayout(GRBackendRenderTarget target)
    {
        bool success = GetImageLayout(target.Handle, out uint layout);
        GC.KeepAlive(target);
        if (!success)
            throw new InvalidOperationException("The Skia render target has no Vulkan image state.");
        return (ImageLayout)layout;
    }

    public static void SetImageLayout(GRBackendRenderTarget target, ImageLayout layout)
    {
        SetImageLayout(target.Handle, (uint)layout);
        GC.KeepAlive(target);
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "gr_beutl_backendrendertarget_get_vk_image_layout")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetImageLayout(nint target, out uint layout);

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "gr_beutl_backendrendertarget_set_vk_image_layout")]
    private static extern void SetImageLayout(nint target, uint layout);
}
