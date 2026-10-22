using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;

using Beutl.Editor.Services;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Vector3 = System.Numerics.Vector3;

namespace Beutl.HeadlessUITests;

public partial class PlayerViewPlaybackInputTests
{
    // Render pixels the cube moves along world X and Y per frame pixel dragged right; one is 1080 / 480 world units.
    [AvaloniaTest]
    [TestCase(50f, 0f, 2f, 0f)]
    [TestCase(100f, 90f, 0f, -1f)]
    public async Task A_3D_drag_keeps_the_object_under_the_pointer_in_a_scaled_or_turned_scene(
        float scale, float rotation, float expectedX, float expectedY)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(100, 100, scale));
        transform.Children.Add(new RotationTransform(rotation));
        scene.Transform.Value = transform;
        editor.GetRequiredService<IEditorSelection>().Select(editor.Scene.Children.Single());
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            Vector3 start = cube.Position.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(40, 0), MouseButton.Left);

            double oneToOneMove = 40 * 640 / view.image.Bounds.Width * (1080.0 / 480.0);
            Vector3 moved = cube.Position.Value - start;
            Assert.Multiple(() =>
            {
                Assert.That(moved.X, Is.EqualTo(expectedX * oneToOneMove).Within(0.5));
                Assert.That(moved.Y, Is.EqualTo(expectedY * oneToOneMove).Within(0.5));
            });
        }
        finally
        {
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        }
    }
}
