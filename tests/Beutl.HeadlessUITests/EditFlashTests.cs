using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EditFlashTests
{
    // Avalonia times an animation started during a frame from that frame's start. A flash started at the end of a
    // slow layout pass, as when an agent edit reopens the property tab, still plays from the frame that draws it.
    [AvaloniaTest]
    public void A_flash_started_after_a_slow_layout_plays_its_pulse()
    {
        var target = new Border { Width = 40, Height = 20 };
        var stalling = new StallingDecorator { Child = target };
        var window = new Window { Content = stalling, Width = 200, Height = 100 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);

            void Flash(object? sender, EventArgs e)
            {
                window.LayoutUpdated -= Flash;
                EditFlash.Run(target);
            }

            // The layout pass outlasts the 1.6 second pulse, and the flash starts as it ends.
            stalling.Stall = TimeSpan.FromSeconds(2);
            window.LayoutUpdated += Flash;
            stalling.InvalidateMeasure();
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render(2);

            Assert.That(AdornerLayer.GetAdornerLayer(target)!.Children.Any(c => AdornerLayer.GetAdornedElement(c) == target),
                Is.True, "The outline still pulses on the frames after that layout.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Spends Stall in its next measure, as a property tab opening on a loaded machine can.
    private sealed class StallingDecorator : Decorator
    {
        public TimeSpan Stall { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            Thread.Sleep(Stall);
            Stall = TimeSpan.Zero;
            return base.MeasureOverride(availableSize);
        }
    }
}
