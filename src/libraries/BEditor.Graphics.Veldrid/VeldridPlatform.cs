using System;

using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Veldrid
{
    public class VeldridGLPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height, global::Veldrid.GraphicsBackend.OpenGL);
        }
    }

    public class VeldridMetalPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(
                width,
                height,
                OperatingSystem.IsMacOS() ? global::Veldrid.GraphicsBackend.Metal : global::Veldrid.GraphicsBackend.OpenGL);
        }
    }

    public class VeldridDirectXPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(
                width,
                height,
                OperatingSystem.IsWindows() ? global::Veldrid.GraphicsBackend.Direct3D11 : global::Veldrid.GraphicsBackend.OpenGL);
        }
    }

    public class VeldridVulkanPlatform : IPlatform
    {
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height, global::Veldrid.GraphicsBackend.Vulkan);
        }
    }
}