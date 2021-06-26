using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.ViewModels.Settings
{
    public sealed class ProjectViewModel
    {
        public const string OPENGL = "OpenGL";
        public const string SKIA = "Skia";
        public const string VELDRID_OPENGL = "Veldrid (OpenGL)";
        public const string VELDRID_METAL = "Veldrid (Metal)";
        public const string VELDRID_DIRECTX = "Veldrid (Direct3D 11)";
        public const string VELDRID_VULKAN = "Veldrid (Vulkan)";

        public string[] Profiles { get; } = new[]
        {
            OPENGL,
            SKIA,
            VELDRID_OPENGL,
            VELDRID_METAL,
            VELDRID_DIRECTX,
            VELDRID_VULKAN,
        };
    }
}