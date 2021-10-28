using System;

namespace BEditor.ViewModels.Settings
{
    public sealed class ProjectViewModel
    {
        public const string OPENGL = "OpenGL";
        public const string SKIA = "Skia";
        public const string DIRECT3D11 = "Direct3D 11";
        public const string METAL = "Metal";
        public const string VULKAN = "Vulkan";

        public const string OPENAL = "OpenAL";
        public const string XAUDIO2 = "XAudio2";

        public ProjectViewModel()
        {
            if (OperatingSystem.IsWindows())
            {
                AudioProfiles = new[]
                {
                    OPENAL,
                    XAUDIO2,
                };
            }
            else
            {
                AudioProfiles = new[]
                {
                    OPENAL,
                };
            }
        }

        public string[] Profiles { get; } = new[]
        {
            OPENGL,
            SKIA,
            DIRECT3D11,
            METAL,
            VULKAN,
        };

        public string[] AudioProfiles { get; }
    }
}