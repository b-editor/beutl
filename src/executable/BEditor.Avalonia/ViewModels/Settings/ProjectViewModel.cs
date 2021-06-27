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
        public const string DIRECT3D11 = "Direct3D 11";
        public const string METAL = "Metal";
        public const string VULKAN = "Vulkan";

        public string[] Profiles { get; } = new[]
        {
            OPENGL,
            SKIA,
            DIRECT3D11,
            METAL,
            VULKAN,
        };
    }
}