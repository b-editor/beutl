using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.ViewModels.Settings
{
    public sealed class InfomationViewModel
    {
        public InfomationViewModel()
        {
            Version = typeof(InfomationViewModel).Assembly.GetName().Version!.ToString(3);
            OperatingSystem = RuntimeInformation.OSDescription;
            Framework = RuntimeInformation.FrameworkDescription;
        }

        public string Version { get; }
        public string OperatingSystem { get; }
        public string Framework { get; }
    }
}