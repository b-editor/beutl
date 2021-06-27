using System;

using BEditor.Graphics.Platform;

using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace BEditor.Graphics.Veldrid
{
    public class VeldridPlatform : IPlatform
    {
        public VeldridPlatform(GraphicsBackend backend)
        {
            var windowInfo = new WindowCreateInfo()
            {
                WindowWidth = 1,
                WindowHeight = 1,
                WindowInitialState = WindowState.Hidden,
            };
            Window = VeldridStartup.CreateWindow(ref windowInfo);

            var options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true
            };

            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(Window, options, backend);
        }

        public Sdl2Window Window { get; }

        public GraphicsDevice GraphicsDevice { get; }

        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height, this);
        }
    }

    public class VeldridMetalPlatform : VeldridPlatform
    {
        public VeldridMetalPlatform()
            : base(OperatingSystem.IsMacOS() ? GraphicsBackend.Metal : VeldridStartup.GetPlatformDefaultBackend())
        {
        }
    }

    public class VeldridDirectXPlatform : VeldridPlatform
    {
        public VeldridDirectXPlatform()
            : base(OperatingSystem.IsWindows() ? GraphicsBackend.Direct3D11 : VeldridStartup.GetPlatformDefaultBackend())
        {
        }
    }

    public class VeldridVulkanPlatform : VeldridPlatform
    {
        public VeldridVulkanPlatform()
            : base(GraphicsBackend.Vulkan)
        {
        }
    }
}