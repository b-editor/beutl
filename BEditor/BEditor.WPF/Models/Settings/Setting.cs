using System.IO;
using System.Xml.Linq;

using BEditor.Core.Data;

namespace BEditor.Models.Settings
{
    public class Setting
    {
        public static double ClipHeight { get; } = BEditor.Settings.Default.ClipHeight;
        public static float WidthOf1Frame { get; } = BEditor.Settings.Default.WidthOf1Frame;
    }
}
