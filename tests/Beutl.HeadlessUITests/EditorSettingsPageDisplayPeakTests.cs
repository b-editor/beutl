using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable] // drives GlobalConfiguration.Instance singletons
public sealed class EditorSettingsPageDisplayPeakTests
{
    // The HDR preview takes the setting from the player, which reads the editor settings, and follows a change.
    [AvaloniaTest]
    public async Task Hdr_preview_follows_the_display_peak_setting()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        float prior = config.DisplayPeakLuminance.Value;
        try
        {
            config.DisplayPeakLuminance.Value = 600f;
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("display-peak");
            // Unattached: headless cannot create the native child window, and the bindings need only the DataContext.
            var image = (HdrBitmapView)PreviewSurface.CreateImage(useHdr: true, "PreviewImage.Value");
            image.DataContext = editor.Player;
            Dispatcher.UIThread.RunJobs();
            Assert.That(image.DisplayPeakLuminance, Is.EqualTo(600f));

            config.DisplayPeakLuminance.Value = 0f;
            Dispatcher.UIThread.RunJobs();
            Assert.That(image.DisplayPeakLuminance, Is.Zero);
        }
        finally
        {
            config.DisplayPeakLuminance.Value = prior;
        }
    }

    // The override goes to the editor settings, which keep it within what PQ carries, and the description names what
    // the display reports, so the user can tell whether its peak needs overriding.
    [AvaloniaTest]
    public void Peak_override_round_trips_and_the_description_names_the_reported_display()
    {
        TestGpu.RequireAnyDevice();

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        float prior = config.DisplayPeakLuminance.Value;
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value;
        try
        {
            using var viewModel = new EditorSettingsPageViewModel();

            viewModel.DisplayPeakLuminance.Value = 600;
            Assert.That(config.DisplayPeakLuminance.Value, Is.EqualTo(600f));

            viewModel.DisplayPeakLuminance.Value = 20000;
            Assert.That(config.DisplayPeakLuminance.Value, Is.EqualTo(10000f));
            Assert.That(viewModel.DisplayPeakLuminance.Value, Is.EqualTo(10000d));

            config.DisplayPeakLuminance.Value = 0f;
            Assert.That(viewModel.DisplayPeakLuminance.Value, Is.Zero);

            viewModel.ShowReportedDisplay(new HdrDisplayReport(SdrWhiteNits: 80f, PeakNits: 486f));
            Assert.That(viewModel.DisplayPeakLuminanceDescription.Value,
                Does.StartWith(SettingsStrings.DisplayPeakLuminance_Description)
                    .And.EndWith(string.Format(SettingsStrings.DisplayPeakLuminance_Reported, 486f, 80f)));

            viewModel.ShowReportedDisplay(null);
            Assert.That(viewModel.DisplayPeakLuminanceDescription.Value,
                Does.StartWith(SettingsStrings.DisplayPeakLuminance_Description)
                    .And.EndWith(SettingsStrings.DisplayPeakLuminance_NotHdr));
        }
        finally
        {
            config.DisplayPeakLuminance.Value = prior;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value = priorGpu;
        }
    }

    // The row shows only where the override applies, which is Windows. With BEUTL_HEADLESS_CAPTURE_DIR set, the
    // shown row is captured for design review.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Peak_override_row_shows_only_where_it_applies(bool supported)
    {
        TestGpu.RequireAnyDevice();

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        float prior = config.DisplayPeakLuminance.Value;
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value;
        config.DisplayPeakLuminance.Value = 1000f;
        using var viewModel = new EditorSettingsPageViewModel { IsDisplayPeakLuminanceSupported = supported };
        var page = new EditorSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            // Only Windows reads the display; elsewhere the description shows what it is given.
            if (!OperatingSystem.IsWindows())
                viewModel.ShowReportedDisplay(new HdrDisplayReport(SdrWhiteNits: 80f, PeakNits: 486f));
            HeadlessTestHelpers.Render(3);

            OptionsDisplayItem row = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                .Single(x => Equals(x.Header, SettingsStrings.DisplayPeakLuminance));
            Assert.That(row.IsEffectivelyVisible, Is.EqualTo(supported));
            if (!supported)
                return;

            row.BringIntoView();
            HeadlessTestHelpers.Render(3);
            var input = (NumericUpDown)row.ActionButton!;
            Assert.That(input.Value, Is.EqualTo(1000m));
            Assert.That(row.Description, Is.EqualTo(viewModel.DisplayPeakLuminanceDescription.Value));
            HeadlessCapture.SaveIfRequested(window, nameof(EditorSettingsPageDisplayPeakTests), "display-peak");
        }
        finally
        {
            window.Close();
            config.DisplayPeakLuminance.Value = prior;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value = priorGpu;
        }
    }
}
