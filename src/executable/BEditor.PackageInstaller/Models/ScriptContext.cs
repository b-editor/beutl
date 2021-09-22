
using BEditor.PackageInstaller.ViewModels;

namespace BEditor.PackageInstaller.Models
{
    public sealed class ScriptContext
    {
        private readonly ModifyPageViewModel _viewModel;
        private readonly string _appDir;
        private readonly string _pluginDir;

        public ScriptContext(ModifyPageViewModel viewModel, string appDir, string pluginDir)
        {
            _viewModel = viewModel;
            _appDir = appDir;
            _pluginDir = pluginDir;
        }

        public void Status(string value)
        {
            _viewModel.Status.Value = value;
        }

        public void Progress(float value)
        {
            if (value < 0)
            {
                _viewModel.IsIndeterminate.Value = true;
            }
            else
            {
                _viewModel.IsIndeterminate.Value = false;
                _viewModel.Progress.Value = _viewModel.Max.Value * value;
            }
        }

        public string AppDirectory()
        {
            return _appDir;
        }

        public string PluginDirectory()
        {
            return _pluginDir;
        }
    }
}
