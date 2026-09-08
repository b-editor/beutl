using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

// The proxy max-size setting is bound as a free-form TextBox. When the user types an over-cap value
// while the config is already at the cap, CoreObject.SetValue raises no change notification, so the
// ViewModel must re-sync the bound ReactiveProperty to the clamped value itself — otherwise the
// TextBox keeps showing the invalid input while the real setting stays clamped.
[TestFixture]
[NonParallelizable] // drives GlobalConfiguration.Instance singletons
public sealed class EditorSettingsPageViewModelTests
{
    [AvaloniaTest]
    public void Over_cap_input_at_the_cap_re_syncs_the_textbox_to_the_clamped_value()
    {
        GpuTestGate.EnsureAvailable();

        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        long priorBytes = config.MaxTotalBytes;
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName;
        config.MaxTotalBytes = ProxyStoreConfig.MaxTotalBytesLimit;

        try
        {
            using var viewModel = new EditorSettingsPageViewModel();

            viewModel.ProxyStoreMaxTotalGiB.Value = 1000d;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ProxyStoreMaxTotalGiB.Value, Is.EqualTo(500d), "TextBox must re-sync to the clamped value.");
                Assert.That(config.MaxTotalBytes, Is.EqualTo(ProxyStoreConfig.MaxTotalBytesLimit));
            });
        }
        finally
        {
            config.MaxTotalBytes = priorBytes;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName = priorGpu;
        }
    }

    [AvaloniaTest]
    public void Over_cap_input_below_the_cap_clamps_and_re_syncs()
    {
        GpuTestGate.EnsureAvailable();

        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        long priorBytes = config.MaxTotalBytes;
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName;
        config.MaxTotalBytes = ProxyStoreConfig.MinTotalBytes;

        try
        {
            using var viewModel = new EditorSettingsPageViewModel();

            viewModel.ProxyStoreMaxTotalGiB.Value = 1000d;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ProxyStoreMaxTotalGiB.Value, Is.EqualTo(500d));
                Assert.That(config.MaxTotalBytes, Is.EqualTo(ProxyStoreConfig.MaxTotalBytesLimit));
            });
        }
        finally
        {
            config.MaxTotalBytes = priorBytes;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName = priorGpu;
        }
    }

    [AvaloniaTest]
    public void Version_control_options_round_trip_through_the_editor_settings_view_model()
    {
        GpuTestGate.EnsureAvailable();

        VersionControlConfig config = GlobalConfiguration.Instance.VersionControlConfig;
        (bool EnableForNewProjects,
            bool AutoCommitOnSave,
            bool AutoCommitOnClose,
            string? GitExecutablePath,
            bool UseLfsWhenAvailable,
            int LargeMediaWarningThresholdMb) previous = (
            config.EnableForNewProjects,
            config.AutoCommitOnSave,
            config.AutoCommitOnClose,
            config.GitExecutablePath,
            config.UseLfsWhenAvailable,
            config.LargeMediaWarningThresholdMb);
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName;

        try
        {
            using var viewModel = new EditorSettingsPageViewModel();

            viewModel.EnableVersionControlForNewProjects.Value = false;
            viewModel.AutoCommitOnSave.Value = false;
            viewModel.AutoCommitOnClose.Value = false;
            viewModel.GitExecutablePath.Value = " /opt/custom/git ";
            viewModel.UseLfsWhenAvailable.Value = false;
            viewModel.LargeMediaWarningThresholdMb.Value = 0;

            Assert.Multiple(() =>
            {
                Assert.That(config.EnableForNewProjects, Is.False);
                Assert.That(config.AutoCommitOnSave, Is.False);
                Assert.That(config.AutoCommitOnClose, Is.False);
                Assert.That(config.GitExecutablePath, Is.EqualTo("/opt/custom/git"));
                Assert.That(config.UseLfsWhenAvailable, Is.False);
                Assert.That(config.LargeMediaWarningThresholdMb, Is.EqualTo(1));
                Assert.That(viewModel.LargeMediaWarningThresholdMb.Value, Is.EqualTo(1));
            });

            viewModel.GitExecutablePath.Value = "  ";
            Assert.That(config.GitExecutablePath, Is.Null);
        }
        finally
        {
            config.EnableForNewProjects = previous.EnableForNewProjects;
            config.AutoCommitOnSave = previous.AutoCommitOnSave;
            config.AutoCommitOnClose = previous.AutoCommitOnClose;
            config.GitExecutablePath = previous.GitExecutablePath;
            config.UseLfsWhenAvailable = previous.UseLfsWhenAvailable;
            config.LargeMediaWarningThresholdMb = previous.LargeMediaWarningThresholdMb;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName = priorGpu;
        }
    }
}
