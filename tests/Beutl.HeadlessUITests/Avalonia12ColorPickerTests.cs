using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using AvaColors = Avalonia.Media.Colors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class Avalonia12ColorPickerTests
{
    [AvaloniaTest]
    [TestCase("#RRGGBB", ColorTextType.Hex)]
    [TestCase("#AARRGGBB", ColorTextType.HexAlpha)]
    [TestCase("rgb( r, g, b )", ColorTextType.RGB)]
    [TestCase("rgba( r, g, b, a )", ColorTextType.RGBA)]
    public void Hex_context_flyout_changes_the_format_and_preserves_the_color(string format, ColorTextType expected)
    {
        var picker = new FAColorPicker { Color = AvaColors.Red, IsCompact = false };
        var window = new Window { Content = picker, Width = 800, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox hex = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "HexBox");
            var menu = (FAMenuFlyout)hex.ContextFlyout!;
            menu.ShowAt(hex);
            HeadlessTestHelpers.Render();
            FAMenuFlyoutItem item = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == format);
            TopLevel popup = TopLevel.GetTopLevel(item)!;
            Point center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), popup)!.Value;
            popup.MouseMove(center);
            popup.MouseDown(center, MouseButton.Left);
            popup.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(picker.ColorTextType, Is.EqualTo(expected));
                Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(AvaColors.Red));
                Assert.That(menu.IsOpen, Is.False);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Numeric_components_update_the_color_and_follow_programmatic_changes()
    {
        var picker = new FAColorPicker
        {
            Color = AvaColors.Red,
            IsCompact = false,
            IsAlphaEnabled = true,
            UseColorWheel = true,
            UseColorTriangle = true,
            UseColorPalette = true
        };
        var window = new Window { Content = picker, Width = 800, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            FANumberBox FindBox(string name) => picker.GetVisualDescendants()
                .OfType<FANumberBox>().Single(box => box.Name == name);

            Assert.That(FindBox("RedBox").Value, Is.EqualTo(255));
            FindBox("RedBox").Value = 0;
            FindBox("BlueBox").Value = 255;
            HeadlessTestHelpers.Settle();
            Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(AvaColors.Blue));

            picker.Color = AvaColors.Lime;
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(FindBox("RedBox").Value, Is.Zero);
                Assert.That(FindBox("GreenBox").Value, Is.EqualTo(255));
                Assert.That(FindBox("BlueBox").Value, Is.Zero);
                Assert.That(FindBox("AlphaBox").Value, Is.EqualTo(255));
            });

            TabControl modes = picker.GetVisualDescendants().OfType<TabControl>()
                .Single(control => control.Name == "DisplayItemTabControl");
            for (int index = 0; index < modes.Items.Count; index++)
            {
                modes.SelectedIndex = index;
                HeadlessTestHelpers.Render();
                Assert.That((Avalonia.Media.Color)picker.Color, Is.EqualTo(AvaColors.Lime),
                    "Switching the spectrum, wheel, triangle, or palette must preserve the selected color.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Color_button_commits_only_when_the_flyout_is_confirmed(bool confirm)
    {
        var pickerButton = new ColorPickerButton
        {
            Color = AvaColors.Red,
            ShowAcceptDismissButtons = true
        };
        var window = new Window { Content = pickerButton, Width = 400, Height = 400 };
        int confirmed = 0;
        int dismissed = 0;
        pickerButton.FlyoutConfirmed += (_, _) => confirmed++;
        pickerButton.FlyoutDismissed += (_, _) => dismissed++;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            pickerButton.GetVisualDescendants().OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();

            // The buttons share a private flyout whose popup is a separate visual tree.
            var flyout = (ColorPickerFlyout)typeof(ColorPickerButton)
                .GetField("_flyout", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
            FAColorPicker picker = flyout.ColorPicker;
            Control presenter = picker.GetVisualAncestors().OfType<FAPickerFlyoutPresenter>().Single();
            picker.Color = AvaColors.Blue;
            Assert.That(pickerButton.Color, Is.EqualTo(AvaColors.Red));
            Button action = presenter.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == (confirm ? "AcceptButton" : "DismissButton"));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(pickerButton.Color, Is.EqualTo(confirm ? AvaColors.Blue : AvaColors.Red));
                Assert.That(confirmed, Is.EqualTo(confirm ? 1 : 0));
                Assert.That(dismissed, Is.EqualTo(confirm ? 0 : 1));
                Assert.That(flyout.IsOpen, Is.False);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Applying a new template must unhook the old hex box. UnhookEvents subscribed it again instead.
    [AvaloniaTest]
    public void A_replaced_hex_box_no_longer_handles_enter()
    {
        var picker = new FAColorPicker { Color = AvaColors.Red, IsCompact = false };
        var window = new Window { Content = picker, Width = 800, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox oldHex = picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "HexBox");
            var template = picker.Template;
            picker.Template = null;
            HeadlessTestHelpers.Render();
            picker.Template = template;
            HeadlessTestHelpers.Render();
            Assert.That(picker.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "HexBox"),
                Is.Not.SameAs(oldHex));

            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            oldHex.RaiseEvent(enter);
            Assert.That(enter.Handled, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    // The spectrum draws its border with its BorderBrush and BorderThickness, as the ramps beside it do.
    [AvaloniaTest]
    public void Spectrum_draws_its_border_with_the_border_brush()
    {
        var spectrum = new ColorSpectrum
        {
            Shape = FluentAvalonia.UI.Controls.ColorSpectrumShape.Spectrum,
            Color = AvaColors.Red,
            BorderBrush = Brushes.Lime,
            BorderThickness = 4
        };
        var window = new Window { Content = spectrum, Width = 200, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Point topLeft = spectrum.TranslatePoint(default, window)!.Value;
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            var inside = new PixelPoint((int)topLeft.X + 50, (int)topLeft.Y + 1);
            Assert.That(HeadlessTestHelpers.ReadPixel(frame, inside), Is.EqualTo((0, 255, 0)));
        }
        finally
        {
            window.Close();
        }
    }
}
