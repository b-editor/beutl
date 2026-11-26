using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Language;
using Beutl.Media.Proxy;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable] // uses the shared test app configuration
public sealed class SettingsPageViewModelTests
{
    // At the cap, an over-cap input raises no change notification, so the view model must re-sync the
    // bound value itself.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Over_cap_input_re_syncs_the_textbox_to_the_clamped_value(bool atCap)
    {
        TestGpu.RequireAnyDevice();

        ProxyStoreConfig config = TestShell.Configuration.ProxyStoreConfig;
        long priorBytes = config.MaxTotalBytes.Value;
        string? priorGpu = TestShell.Configuration.GraphicsConfig.SelectedGpuName.Value;
        config.MaxTotalBytes.Value = atCap ? ProxyStoreConfig.MaxTotalBytesLimit : ProxyStoreConfig.MinTotalBytes;

        try
        {
            using var viewModel = new PerformanceSettingsPageViewModel(TestShell.Configuration);

            viewModel.ProxyStoreMaxTotalGiB.Value = 1000d;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ProxyStoreMaxTotalGiB.Value, Is.EqualTo(500d), "TextBox must re-sync to the clamped value.");
                Assert.That(config.MaxTotalBytes.Value, Is.EqualTo(ProxyStoreConfig.MaxTotalBytesLimit));
            });
        }
        finally
        {
            config.MaxTotalBytes.Value = priorBytes;
            TestShell.Configuration.GraphicsConfig.SelectedGpuName.Value = priorGpu;
        }
    }

    [AvaloniaTest]
    public void Timeline_and_property_controls_write_editor_config()
    {
        var configuration = new GlobalConfiguration();
        EditorConfig config = configuration.EditorConfig;
        using var viewModel = new EditorSettingsPageViewModel(configuration);
        var page = new EditorSettingsPage { DataContext = viewModel };
        using var host = new ControlHost(page, 900, 700);

        CheckToggle(page, SettingsStrings.AutoAdjustSceneDuration, config.AutoAdjustSceneDuration);
        CheckToggle(page, SettingsStrings.SwapTimelineScrollDirection, config.SwapTimelineScrollDirection);
        CheckToggle(page, SettingsStrings.SeekOnTimelineClick, config.SeekOnTimelineClick);
        CheckToggle(page, SettingsStrings.ClampResizeToOriginalLength, config.ClampResizeToOriginalLength);
        CheckToggle(page, SettingsStrings.ShowTimelineThumbnailsByDefault, config.ShowTimelineThumbnailsByDefault);
        CheckToggle(page, SettingsStrings.EnablePointerLockInProperty, config.EnablePointerLockInProperty);

        var autoScroll = Input<ComboBox>(page, SettingsStrings.TimelineAutoScrollMode);
        Assert.That(autoScroll.SelectedIndex, Is.EqualTo((int)config.TimelineAutoScrollMode.Value));
        autoScroll.SelectedIndex = (int)TimelineAutoScrollMode.AlwaysFollow;
        Assert.That(config.TimelineAutoScrollMode.Value, Is.EqualTo(TimelineAutoScrollMode.AlwaysFollow));
    }

    // The tone mapping list starts with Auto, then lists the modes as before Auto existed. It shows the mode the settings
    // hold and writes the one picked, although the list's order differs from the modes' numbers.
    [AvaloniaTest]
    public void The_tone_mapping_list_starts_with_auto()
    {
        EditorConfig config = TestShell.Configuration.EditorConfig;
        UIToneMappingOperator prior = config.ToneMappingMode.Value;
        config.ToneMappingMode.Value = UIToneMappingOperator.Auto;
        using var viewModel = new PreviewSettingsPageViewModel(config);
        var page = new PreviewSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            OptionsDisplayItem row = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                .Single(x => Equals(x.Header, SettingsStrings.ToneMappingMode));
            row.BringIntoView();
            HeadlessTestHelpers.Render(3);
            var comboBox = (ComboBox)row.ActionButton!;

            Assert.That(comboBox.Items.Cast<ComboBoxItem>().Select(x => x.Content), Is.EqualTo(new object[]
            {
                SettingsStrings.ToneMappingMode_Auto,
                SettingsStrings.ToneMappingMode_None,
                SettingsStrings.ToneMappingMode_Reinhard,
                SettingsStrings.ToneMappingMode_ACES,
                SettingsStrings.ToneMappingMode_Hable,
                SettingsStrings.ToneMappingMode_FitToDisplay
            }));
            Assert.That(comboBox.SelectedIndex, Is.Zero);

            config.ToneMappingMode.Value = UIToneMappingOperator.None;
            HeadlessTestHelpers.Render(1);
            Assert.That(comboBox.SelectedIndex, Is.EqualTo(1));

            comboBox.SelectedIndex = 5;
            Assert.That(config.ToneMappingMode.Value, Is.EqualTo(UIToneMappingOperator.FitToDisplay));

            comboBox.SelectedIndex = 0;
            Assert.That(config.ToneMappingMode.Value, Is.EqualTo(UIToneMappingOperator.Auto));
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(SettingsPageViewModelTests), "tone-mapping");
            comboBox.IsDropDownOpen = true;
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(SettingsPageViewModelTests), "tone-mapping-list");
            comboBox.IsDropDownOpen = false;
        }
        finally
        {
            window.Close();
            config.ToneMappingMode.Value = prior;
        }
    }

    [AvaloniaTest]
    public void Preview_source_hdr_and_exposure_controls_write_editor_config()
    {
        var config = new EditorConfig();
        using var viewModel = new PreviewSettingsPageViewModel(config);
        var page = new PreviewSettingsPage { DataContext = viewModel };
        using var host = new ControlHost(page);

        CheckToggle(page, SettingsStrings.UseHdrPreview, config.UseHdrPreview);
        var source = Input<ComboBox>(page, Strings.PreviewSource);
        Assert.That(source.SelectedIndex, Is.EqualTo((int)PreviewSourceMode.PreferProxy));
        source.SelectedIndex = (int)PreviewSourceMode.ForceOriginal;
        Assert.That(config.PreviewSourceMode.Value, Is.EqualTo(PreviewSourceMode.ForceOriginal));

        Slider exposure = Input<StackPanel>(page, SettingsStrings.ToneMappingExposure).Children.OfType<Slider>().Single();
        Assert.That(exposure.Value, Is.EqualTo(config.ToneMappingExposure.Value));
        exposure.Value = 1.5;
        Assert.That(config.ToneMappingExposure.Value, Is.EqualTo(1.5f));
    }

    [AvaloniaTest]
    public void Performance_controls_write_graphics_cache_and_proxy_config()
    {
        TestGpu.RequireAnyDevice();

        var configuration = new GlobalConfiguration();
        EditorConfig cache = configuration.EditorConfig;
        ProxyStoreConfig proxies = configuration.ProxyStoreConfig;
        using var viewModel = new PerformanceSettingsPageViewModel(configuration);
        var page = new PerformanceSettingsPage { DataContext = viewModel };
        using var host = new ControlHost(page);

        var gpu = Input<ComboBox>(page, SettingsStrings.SelectedGpu);
        Assert.That(gpu.SelectedIndex, Is.Zero);
        gpu.SelectedIndex = 1;
        Assert.That(configuration.GraphicsConfig.SelectedGpuName.Value, Is.EqualTo(((GpuItem)gpu.Items[1]!).Name));
        gpu.SelectedIndex = 0;
        Assert.That(configuration.GraphicsConfig.SelectedGpuName.Value, Is.Null);

        CheckToggle(page, SettingsStrings.EnableFrameCache, cache.IsFrameCacheEnabled);
        CheckToggle(page, SettingsStrings.EnableNodeCache, cache.IsNodeCacheEnabled);
        Input<TextBox>(page, SettingsStrings.FrameCacheMaxSize).Text = "128";
        Input<ComboBox>(page, SettingsStrings.FrameCacheScale).SelectedIndex = (int)FrameCacheConfigScale.Half;
        Input<ComboBox>(page, SettingsStrings.FrameCacheColorType).SelectedIndex = (int)FrameCacheConfigColorType.YUV;
        Input<NumericUpDown>(page, SettingsStrings.NodeCacheMaxPixels).Value = 500_000;
        Input<NumericUpDown>(page, SettingsStrings.NodeCacheMinPixels).Value = 40;

        string proxyPath = Path.Combine(Path.GetTempPath(), "beutl-test-proxies");
        Input<TextBox>(page, SettingsStrings.ProxyStoreRootPath).Text = proxyPath;
        Input<TextBox>(page, SettingsStrings.ProxyStoreMaxTotalGiB).Text = "20";
        Input<ComboBox>(page, SettingsStrings.ProxyDefaultPreset).SelectedItem = ProxyPreset.Eighth;

        Assert.Multiple(() =>
        {
            Assert.That(cache.FrameCacheMaxSize.Value, Is.EqualTo(128));
            Assert.That(cache.FrameCacheScale.Value, Is.EqualTo(FrameCacheConfigScale.Half));
            Assert.That(cache.FrameCacheColorType.Value, Is.EqualTo(FrameCacheConfigColorType.YUV));
            Assert.That(cache.NodeCacheMaxPixels.Value, Is.EqualTo(500_000));
            Assert.That(cache.NodeCacheMinPixels.Value, Is.EqualTo(40));
            Assert.That(proxies.StoreRootPath.Value, Is.EqualTo(proxyPath));
            Assert.That(proxies.MaxTotalBytes.Value, Is.EqualTo(20L * 1024 * 1024 * 1024));
            Assert.That(proxies.DefaultPreset.Value, Is.EqualTo((int)ProxyPreset.Eighth));
        });
    }

    [AvaloniaTest]
    public void Version_control_options_round_trip_through_their_page()
    {
        var config = new VersionControlConfig();
        using var viewModel = new VersionControlSettingsPageViewModel(config);
        var page = new VersionControlSettingsPage { DataContext = viewModel };
        using var host = new ControlHost(page);

        CheckToggle(page, SettingsStrings.VersionControl_EnableForNewProjects, config.EnableForNewProjects);
        CheckToggle(page, SettingsStrings.VersionControl_AutoCommitOnSave, config.AutoCommitOnSave);
        CheckToggle(page, SettingsStrings.VersionControl_AutoCommitOnClose, config.AutoCommitOnClose);
        CheckToggle(page, SettingsStrings.VersionControl_UseLfsWhenAvailable, config.UseLfsWhenAvailable);

        var gitPath = Input<TextBox>(page, SettingsStrings.VersionControl_GitExecutablePath);
        Assert.That(gitPath.Text, Is.Null.Or.Empty);
        gitPath.Text = " /opt/custom/git ";
        Assert.That(config.GitExecutablePath.Value, Is.EqualTo("/opt/custom/git"));
        gitPath.Text = "  ";
        Assert.That(config.GitExecutablePath.Value, Is.Null);
        Assert.That(viewModel.GitExecutablePath.Value, Is.Empty);

        var threshold = Input<NumericUpDown>(page, SettingsStrings.VersionControl_LargeMediaWarningThresholdMb);
        Assert.That(threshold.Value, Is.EqualTo(50));
        threshold.Value = 75;
        Assert.That(config.LargeMediaWarningThresholdMb.Value, Is.EqualTo(75));
        viewModel.LargeMediaWarningThresholdMb.Value = 0;
        Assert.That(config.LargeMediaWarningThresholdMb.Value, Is.EqualTo(1));
        Assert.That(threshold.Value, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void Privacy_controls_write_telemetry_config()
    {
        var config = new TelemetryConfig();
        using var viewModel = new PrivacySettingsPageViewModel(config);
        var page = new PrivacySettingsPage { DataContext = viewModel };
        using var host = new ControlHost(page);

        var application = Input<ToggleSwitch>(page, SettingsStrings.Telemetry_Beutl_Application);
        var packageManagement = Input<ToggleSwitch>(page, SettingsStrings.Telemetry_Beutl_PackageManagement);
        var apiClient = Input<ToggleSwitch>(page, SettingsStrings.Telemetry_Beutl_Api_Client);
        var logging = Input<ToggleSwitch>(page, SettingsStrings.Telemetry_Beutl_Logging);
        application.IsChecked = true;
        packageManagement.IsChecked = true;
        apiClient.IsChecked = true;
        Assert.Multiple(() =>
        {
            Assert.That(config.Beutl_Application.Value, Is.True);
            Assert.That(config.Beutl_PackageManagement.Value, Is.True);
            Assert.That(config.Beutl_Api_Client.Value, Is.True);
            Assert.That(config.Beutl_Logging.Value, Is.False);
        });

        application.IsChecked = false;
        logging.IsChecked = true;
        Assert.Multiple(() =>
        {
            Assert.That(config.Beutl_Logging.Value, Is.True);
            Assert.That(config.Beutl_Application.Value, Is.True);
            Assert.That(application.IsEffectivelyEnabled, Is.False);
            Assert.That(packageManagement.IsEffectivelyEnabled, Is.False);
            Assert.That(apiClient.IsEffectivelyEnabled, Is.False);
        });
    }

    private static T Input<T>(Control page, string title) where T : Control
        => (T)page.GetVisualDescendants().OfType<OptionsDisplayItem>()
            .Single(row => Equals(row.Header, title)).ActionButton!;

    private static void CheckToggle(Control page, string title, IProperty<bool> setting)
    {
        var toggle = Input<ToggleSwitch>(page, title);
        bool oldValue = setting.Value;
        Assert.That(toggle.IsChecked, Is.EqualTo(oldValue), title);
        toggle.IsChecked = !oldValue;
        Assert.That(setting.Value, Is.EqualTo(!oldValue), title);
    }
}
