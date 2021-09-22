using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.ViewModels.Dialogs
{
    public sealed class AboutBEditorViewModel
    {
        public AboutBEditorViewModel()
        {
            Version = typeof(AboutBEditorViewModel).Assembly.GetName().Version!.ToString(3);
            OperatingSystem = RuntimeInformation.OSDescription;
            Framework = RuntimeInformation.FrameworkDescription;
        }

        public string Version { get; }

        public string OperatingSystem { get; }

        public string Framework { get; }
    }
}
