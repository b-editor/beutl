using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class ToolTipScrollTests
{
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Wheel_continues_scrolling_when_a_tooltip_opens_over_the_pointer(bool overlay)
    {
        var rows = new StackPanel();
        foreach (string name in new[] { "Position", "Scale", "Rotation", "Opacity", "Origin", "Fill", "Stroke", "Shadow" })
            rows.Children.Add(new TextBlock { Text = name, Padding = new Thickness(12), Height = 48 });

        var owner = (TextBlock)rows.Children[3];
        owner.Background = Brushes.Transparent;
        ToolTip.SetTip(owner, "Opacity of the selected element");
        ToolTip.SetShowDelay(owner, 0);
        ToolTip.SetPlacement(owner, PlacementMode.Center);
        ToolTip.SetVerticalOffset(owner, 0);

        var scroll = new ScrollViewer { Content = rows };
        var window = new Window { Content = scroll, Width = 320, Height = 200 };
        bool previous = TestAppBuilder.HeadlessOptions.OverlayPopups;
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlay;
            window.Show();
            HeadlessTestHelpers.Render();
            Point pointer = HeadlessTestHelpers.Center(rows.Children[2], window);
            var delta = new Vector(0, -1);
            window.MouseMove(pointer);
            Assert.That(ToolTip.GetIsOpen(owner), Is.False);

            window.MouseWheel(pointer, delta);
            HeadlessTestHelpers.Render(3);
            double firstOffset = scroll.Offset.Y;
            Assert.That(firstOffset, Is.GreaterThan(0));
            Assert.That(ToolTip.GetIsOpen(owner), Is.True);
            var tip = (ToolTip)window.OpenedPopups.Single().Child!;
            Assert.That(new Rect(tip.Bounds.Size).Contains(tip.PointToClient(window.PointToScreen(pointer))), Is.True);
            HeadlessCapture.SaveIfRequested(tip, nameof(ToolTipScrollTests), overlay ? "overlay" : "native");

            if (!overlay)
            {
                // Headless targets a window directly. On desktop this flag passes input through the native popup.
                var popupRoot = (PopupRoot)TopLevel.GetTopLevel(tip)!;
                Assert.That(popupRoot.IsHitTestVisible, Is.False);
            }

            window.MouseWheel(pointer, delta);
            HeadlessTestHelpers.Render();
            Assert.That(scroll.Offset.Y, Is.EqualTo(firstOffset * 2));
            HeadlessCapture.SaveIfRequested(window, nameof(ToolTipScrollTests), overlay ? "overlay-scrolled" : "native-scrolled");
        }
        finally
        {
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previous;
        }
    }
}
