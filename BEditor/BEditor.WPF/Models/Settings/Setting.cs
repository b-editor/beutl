using System.IO;
using System.Xml.Linq;

using BEditor.Core.Data;

namespace BEditor.Models.Settings {
    public class Setting {
        public static double ClipHeight { get; } = Properties.Settings.Default.ClipHeight;
        public static float WidthOf1Frame { get; } = Properties.Settings.Default.WidthOf1Frame;
    }
}
