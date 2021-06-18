using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.ViewModels.Settings
{
    public sealed class ProjectViewModel
    {
        public string[] Profiles { get; } = new[]
        {
            "OpenGL",
            "Skia",
        };
    }
}
