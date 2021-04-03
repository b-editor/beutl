using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Models
{
    public static class ConstantSettings
    {
        public static double ClipHeight { get; } = Settings.Default.ClipHeight;
        public static float WidthOf1Frame { get; } = Settings.Default.WidthOf1Frame;
    }
}
