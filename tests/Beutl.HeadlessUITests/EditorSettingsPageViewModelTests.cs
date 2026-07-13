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
}
