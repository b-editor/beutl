using System.Runtime.InteropServices;

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