using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Controls.Styling.Themes;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

// PNG captures of the timeline's tool mode, ripple and snap states for design review. They change only the view
// model and the settings, so the same test also captures a build from before the bar above the layer headers.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public sealed class TimelineToolBarCaptureTests
{
    [AvaloniaTest]
    [TestCase(false, 275)]
    [TestCase(false, 200)]
    [TestCase(true, 275)]
    [TestCase(true, 200)]
    public async Task Capture_tool_states(bool light, int headerWidth)
    {
        ThemeVariant theme = light ? ThemeVariant.Light : ThemeVariant.Dark;
        ThemeCaptureTests.UseCaptureTheme(theme, light ? null : BeutlDarkBorderTheme.AccentColor);
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool ripple = config.IsRippleEnabled.Value;
        bool snap = config.IsTimelineSnapEnabled.Value;
        string prefix = $"timeline-toolbar-{(light ? "light" : "dark")}-{headerWidth}";
        try
        {
            using TimelineTestHost host = await TimelineTestHost.CreateAsync(
                prefix,
                MediaTime.FromSeconds(4),
                [
                    new ElementDescription(MediaTime.FromSeconds(0.4), MediaTime.FromSeconds(1.6), 0,
                        new ElementSource.EngineObject(() => new RectShape())),
                    new ElementDescription(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 1,
                        new ElementSource.EngineObject(() => new RectShape())),
                ],
                width: 720,
                theme: theme);
            host.Window.Height = 200;
            host.Window.SetRenderScaling(2);
            host.View.FindControl<Grid>("grid")!.ColumnDefinitions[0].Width = new GridLength(headerWidth);

            (string Name, Action Apply)[] states =
            [
                ("selection", () => { }),
                ("razor", () => host.Model.IsRazorMode.Value = true),
                ("slip", () => host.Model.IsSlipMode.Value = true),
                ("roll", () => host.Model.IsRollMode.Value = true),
                ("slide", () => host.Model.IsSlideMode.Value = true),
                ("ripple-on", () => config.IsRippleEnabled.Value = true),
                ("snap-off", () => config.IsTimelineSnapEnabled.Value = false),
            ];
            foreach ((string name, Action apply) in states)
            {
                host.Model.IsRazorMode.Value = false;
                host.Model.IsSlipMode.Value = false;
                host.Model.IsRollMode.Value = false;
                host.Model.IsSlideMode.Value = false;
                config.IsRippleEnabled.Value = false;
                config.IsTimelineSnapEnabled.Value = true;
                apply();
                HeadlessTestHelpers.Render(3);
                ThemeCaptureTests.Capture(host.Window, $"{prefix}-{name}.png");
            }

            // A build without the bar has no button to show a tip for.
            if (host.View.FindControl<ToggleButton>("RippleButton") is { } rippleButton)
            {
                config.IsRippleEnabled.Value = false;
                config.IsTimelineSnapEnabled.Value = true;
                host.Window.Height = 260;
                ToolTip.SetIsOpen(rippleButton, true);
                HeadlessTestHelpers.Render(3);
                ThemeCaptureTests.Capture(host.Window, $"{prefix}-ripple-tooltip.png");
                ToolTip.SetIsOpen(rippleButton, false);
            }
        }
        finally
        {
            config.IsRippleEnabled.Value = ripple;
            config.IsTimelineSnapEnabled.Value = snap;
            HeadlessTestHelpers.Settle();
        }
    }
}
