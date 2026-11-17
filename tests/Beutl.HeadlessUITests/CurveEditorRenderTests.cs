using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.Curves;
using Beutl.Graphics;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CurveEditorRenderTests
{
    // The editor draws a list that also changes from outside, as an undo or the curves tab's color range changes it,
    // with nothing else about the editor changing.
    [AvaloniaTest]
    public void A_change_to_the_points_from_outside_redraws_the_curve()
    {
        var points = new ObservableCollection<CurveControlPoint> { new(0, 0.5f), new(1, 0.5f) };
        var editor = new CurveEditor { Points = points };
        var window = new Window { Content = editor, Width = 120, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);
            byte[] before = HeadlessTestHelpers.CaptureFramePixels(window).Pixels;

            points.Insert(1, new CurveControlPoint(0.5f, 1));
            HeadlessTestHelpers.Render(2);
            byte[] after = HeadlessTestHelpers.CaptureFramePixels(window).Pixels;

            Assert.That(after.AsSpan().SequenceEqual(before), Is.False, "The curve was not drawn again.");
        }
        finally
        {
            window.Close();
        }
    }
}
