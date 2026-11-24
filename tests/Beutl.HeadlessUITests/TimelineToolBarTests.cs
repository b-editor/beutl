using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The bar above the layer headers, which shows and changes the tool mode, ripple edit and snapping.
[TestFixture]
public class TimelineToolBarTests
{
    private static readonly string[] s_buttonNames =
        ["RazorModeButton", "SlipModeButton", "RollModeButton", "SlideModeButton", "RippleButton", "SnapButton"];

    [AvaloniaTest]
    public async Task A_mode_button_turns_on_its_mode_alone_and_a_second_click_returns_to_the_selection_tool()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-toolbar-modes", MediaTime.FromSeconds(2));
        (ToggleButton Button, ReactivePropertySlim<bool> Mode)[] modes =
        [
            (FindButton(host, "RazorModeButton"), host.Model.IsRazorMode),
            (FindButton(host, "SlipModeButton"), host.Model.IsSlipMode),
            (FindButton(host, "RollModeButton"), host.Model.IsRollMode),
            (FindButton(host, "SlideModeButton"), host.Model.IsSlideMode),
        ];

        foreach ((ToggleButton button, _) in modes)
        {
            Click(host, button);
            Assert.Multiple(() =>
            {
                foreach ((ToggleButton other, ReactivePropertySlim<bool> mode) in modes)
                {
                    bool on = ReferenceEquals(other, button);
                    Assert.That(mode.Value, Is.EqualTo(on), $"{other.Name}'s mode after a click on {button.Name}");
                    Assert.That(other.IsChecked, Is.EqualTo(on), $"{other.Name} after a click on {button.Name}");
                }
            });
        }

        // Off the first press's point, or the two presses would make a double click.
        Click(host, modes[^1].Button, new Vector(6, 0));
        Assert.Multiple(() =>
        {
            Assert.That(modes.Select(m => m.Mode.Value), Is.All.False, "A second click returns to the selection tool.");
            Assert.That(modes.Select(m => m.Button.IsChecked), Is.All.EqualTo(false));
        });

        // The keys and the right-click menu change the mode on the view model, and the buttons follow.
        host.Model.IsRollMode.Value = true;
        HeadlessTestHelpers.Render();
        Assert.That(modes.Select(m => m.Button.IsChecked), Is.EqualTo(new bool?[] { false, false, true, false }));
    }

    [AvaloniaTest]
    public async Task The_ripple_and_snap_buttons_change_the_saved_settings_and_follow_them()
    {
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool ripple = config.IsRippleEnabled.Value;
        bool snap = config.IsTimelineSnapEnabled.Value;
        config.IsRippleEnabled.Value = false;
        config.IsTimelineSnapEnabled.Value = true;
        try
        {
            using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-toolbar-toggles", MediaTime.FromSeconds(2));
            ToggleButton rippleButton = FindButton(host, "RippleButton");
            ToggleButton snapButton = FindButton(host, "SnapButton");
            Assert.Multiple(() =>
            {
                Assert.That(rippleButton.IsChecked, Is.False);
                Assert.That(snapButton.IsChecked, Is.True);
            });

            Click(host, rippleButton);
            Click(host, snapButton);
            Assert.Multiple(() =>
            {
                Assert.That(config.IsRippleEnabled.Value, Is.True);
                Assert.That(config.IsTimelineSnapEnabled.Value, Is.False);
                Assert.That(host.Model.IsRippleEnabled.Value, Is.True);
                Assert.That(host.Model.IsSnapEnabled.Value, Is.False);
            });

            // The B key, the right-click menu and the settings change them too, and the buttons follow.
            config.IsRippleEnabled.Value = false;
            config.IsTimelineSnapEnabled.Value = true;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(rippleButton.IsChecked, Is.False);
                Assert.That(snapButton.IsChecked, Is.True);
            });
        }
        finally
        {
            config.IsRippleEnabled.Value = ripple;
            config.IsTimelineSnapEnabled.Value = snap;
            HeadlessTestHelpers.Settle();
        }
    }

    // The header column can be dragged down to 200 px. A second row of buttons would push the layers below the ruler.
    [AvaloniaTest]
    [TestCase(200)]
    [TestCase(275)]
    public async Task The_buttons_stay_on_one_row_as_tall_as_the_ruler(double headerWidth)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-toolbar-layout", MediaTime.FromSeconds(2));
        host.View.FindControl<Grid>("grid")!.ColumnDefinitions[0].Width = new GridLength(headerWidth);
        HeadlessTestHelpers.Render();
        Control bar = host.View.FindControl<Control>("ToolBar")!;
        Rect[] boxes = [.. s_buttonNames.Select(name => Box(FindButton(host, name), host.View))];

        Assert.Multiple(() =>
        {
            Assert.That(bar.Bounds.Width, Is.EqualTo(headerWidth));
            Assert.That(bar.Bounds.Height, Is.EqualTo(38));
            Assert.That(host.RulerBar.Bounds.Height, Is.EqualTo(38));
            Assert.That(host.Content.TranslatePoint(default, host.View)!.Value.Y, Is.EqualTo(38),
                "The layers start right under the bar.");
            for (int i = 0; i < boxes.Length; i++)
            {
                Assert.That(boxes[i].Size, Is.EqualTo(new Size(28, 28)), s_buttonNames[i]);
                Assert.That(boxes[i].Top, Is.EqualTo(boxes[0].Top), $"{s_buttonNames[i]} is on the first row.");
                Assert.That(boxes[i].Left, Is.GreaterThanOrEqualTo(4), s_buttonNames[i]);
                Assert.That(boxes[i].Right, Is.LessThanOrEqualTo(headerWidth - 4), s_buttonNames[i]);
                if (i > 0)
                    Assert.That(boxes[i].Left, Is.GreaterThanOrEqualTo(boxes[i - 1].Right), s_buttonNames[i]);
            }

            Assert.That(boxes[^1].Right, Is.EqualTo(headerWidth - 4), "Ripple and snap keep to the right.");
        });
    }

    // Clicking a button leaves the keyboard focus with the timeline, so Space does not press the button and the
    // timeline's keys still reach its commands.
    [AvaloniaTest]
    public async Task A_click_on_a_button_leaves_the_keys_with_the_timeline()
    {
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool snap = config.IsTimelineSnapEnabled.Value;
        config.IsTimelineSnapEnabled.Value = true;
        try
        {
            using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-toolbar-keys", MediaTime.FromSeconds(2));
            // As the tool tab does in the app (ToolTabContent), the view takes the timeline's keys.
            AppHelper.GetContextCommandManager!.Invoke()!.Attach(host.View, TimelineTabExtension.Instance);
            Assert.That(host.View.Focus(), Is.True);
            ToggleButton snapButton = FindButton(host, "SnapButton");

            Click(host, snapButton);
            Assert.Multiple(() =>
            {
                Assert.That(config.IsTimelineSnapEnabled.Value, Is.False);
                Assert.That(host.Window.FocusManager!.GetFocusedElement(), Is.SameAs(host.View));
            });

            // Checked after each key: pressed together, two toggles would cancel out.
            PressKey(host, Key.Space, PhysicalKey.Space, " ");
            Assert.That(config.IsTimelineSnapEnabled.Value, Is.False, "Space does not press the button.");
            PressKey(host, Key.Enter, PhysicalKey.Enter, null);
            Assert.That(config.IsTimelineSnapEnabled.Value, Is.False, "Enter does not press the button.");

            PressKey(host, Key.C, PhysicalKey.C, "c");
            Assert.Multiple(() =>
            {
                Assert.That(host.Model.IsRazorMode.Value, Is.True, "C reaches the timeline's command.");
                Assert.That(FindButton(host, "RazorModeButton").IsChecked, Is.True);
            });
        }
        finally
        {
            config.IsTimelineSnapEnabled.Value = snap;
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_tooltips_give_the_name_the_key_and_what_the_button_does()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-toolbar-tips", MediaTime.FromSeconds(2));
        (string Button, string Title, string Description)[] tips =
        [
            ("RazorModeButton", $"{Strings.RazorTool} (C)", Strings.RazorTool_Description),
            ("SlipModeButton", $"{Strings.SlipTool} (S)", Strings.SlipTool_Description),
            ("RollModeButton", $"{Strings.RollTool} (R)", Strings.RollTool_Description),
            ("SlideModeButton", $"{Strings.SlideTool} (D)", Strings.SlideTool_Description),
            ("RippleButton", $"{Strings.RippleEdit} (B)", Strings.RippleEdit_Description),
            ("SnapButton", Strings.TimelineSnap, Strings.TimelineSnap_Description),
        ];

        foreach ((string name, string title, string description) in tips)
        {
            ToggleButton button = FindButton(host, name);
            ToolTip.SetIsOpen(button, true);
            HeadlessTestHelpers.Render();
            var tip = (StackPanel)ToolTip.GetTip(button)!;
            Assert.Multiple(() =>
            {
                Assert.That(ToolTip.GetIsOpen(button), Is.True, name);
                Assert.That(tip.Children.OfType<TextBlock>().Select(t => t.Text), Is.EqualTo(new[] { title, description }), name);
            });
            ToolTip.SetIsOpen(button, false);
            HeadlessTestHelpers.Render();
        }
    }

    private static void PressKey(TimelineTestHost host, Key key, PhysicalKey physicalKey, string? symbol)
    {
        host.Window.KeyPress(key, RawInputModifiers.None, physicalKey, symbol);
        host.Window.KeyRelease(key, RawInputModifiers.None, physicalKey, symbol);
        HeadlessTestHelpers.Render();
    }

    private static ToggleButton FindButton(TimelineTestHost host, string name)
        => host.View.FindControl<ToggleButton>(name) ?? throw new InvalidOperationException($"No {name}.");

    private static Rect Box(Visual visual, Visual relativeTo)
        => new(visual.TranslatePoint(default, relativeTo)!.Value, visual.Bounds.Size);

    private static void Click(TimelineTestHost host, ToggleButton button, Vector offset = default)
    {
        Point point = HeadlessTestHelpers.Center(button, host.Window) + offset;
        TimelineTestHost.WaitForHitTest(host.Window, button, point);
        host.Window.MouseMove(point);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Render();
    }
}
