using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Controls.Curves;
using Beutl.Graphics;
using Beutl.Testing.Headless;
using AvaPoint = Avalonia.Point;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CurveEditorDragTests
{
    // Sorting moves the dragged point past its neighbor; the drag must stay on that point.
    [AvaloniaTest]
    public void Dragging_a_point_past_its_neighbor_keeps_dragging_that_point()
    {
        var points = new List<CurveControlPoint> { new(0, 0), new(0.3f, 0.3f), new(0.6f, 0.6f), new(1, 1) };
        var editor = new CurveEditor { Points = points };
        var window = new Window { Content = editor, Width = 200, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            AvaPoint At(float x, float y)
                => editor.TranslatePoint(new AvaPoint(x * editor.Bounds.Width, (1 - y) * editor.Bounds.Height), window)!.Value;

            window.MouseDown(At(0.3f, 0.3f), MouseButton.Left);
            window.MouseMove(At(0.8f, 0.3f));
            window.MouseMove(At(0.9f, 0.5f));
            window.MouseUp(At(0.9f, 0.5f), MouseButton.Left);

            Assert.That(points.Select(p => p.Point), Is.EqualTo(new[]
            {
                new BtlPoint(0, 0), new BtlPoint(0.6f, 0.6f), new BtlPoint(0.9f, 0.5f), new BtlPoint(1, 1)
            }));
        }
        finally
        {
            window.Close();
        }
    }
}
