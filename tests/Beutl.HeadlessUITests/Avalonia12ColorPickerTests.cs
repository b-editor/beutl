using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
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
}
