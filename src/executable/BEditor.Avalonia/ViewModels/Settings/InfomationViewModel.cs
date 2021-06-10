using System.Runtime.InteropServices;

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