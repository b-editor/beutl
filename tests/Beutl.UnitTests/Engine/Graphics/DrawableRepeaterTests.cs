using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
[NonParallelizable]
public sealed class DrawableRepeaterTests
{
    private const int Frame = 96;
    private const int Dot = 8;
    private const int Pitch = 24;

    // The green of every pixel, on black.
    private static byte[,] Render(Drawable drawable)
    {
        var greens = new byte[Frame, Frame];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(Frame, Frame), 1f);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            for (int y = 0; y < Frame; y++)
            {
                ReadOnlySpan<byte> row = bytes.GetRow(y);
                for (int x = 0; x < Frame; x++)
                    greens[y, x] = row[x * 4 + 1];
            }
        });
        return greens;
    }

    private static RectShape WhiteDot()
    {
        var dot = new RectShape { Width = { Value = Dot }, Height = { Value = Dot } };
        dot.Fill.Value = new SolidColorBrush(Colors.White);
        return dot;
    }

    private static DrawableRepeater Repeat(Drawable child, int columns, int rows)
    {
        var repeater = new DrawableRepeater
        {
            Columns = { Value = columns },
            Rows = { Value = rows },
            Spacing = { Value = new Size(Pitch, Pitch) },
        };
        repeater.Children.Add(child);
        return repeater;
    }

    private static bool LitAt(byte[,] greens, int offsetX, int offsetY)
        => greens[Frame / 2 + offsetY, Frame / 2 + offsetX] > 200;

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [Test]
    public void Every_cell_gets_a_copy_with_the_grid_around_where_the_child_is()
    {
        byte[,] greens = Render(Repeat(WhiteDot(), 3, 2));

        Assert.Multiple(() =>
        {
            foreach (int x in new[] { -Pitch, 0, Pitch })
                foreach (int y in new[] { -Pitch / 2, Pitch / 2 })
                    Assert.That(LitAt(greens, x, y), Is.True, $"at {x}, {y}");

            Assert.That(LitAt(greens, 0, 0), Is.False, "between the rows");
            Assert.That(LitAt(greens, Pitch + Dot, Pitch / 2), Is.False, "past the last column");
        });
    }

    [Test]
    public void One_cell_draws_what_the_child_draws()
    {
        Assert.That(Render(Repeat(WhiteDot(), 1, 1)), Is.EqualTo(Render(WhiteDot())));
    }

    [Test]
    public void The_offset_moves_the_cells_and_one_leaving_comes_back_at_the_other_end()
    {
        DrawableRepeater moved = Repeat(WhiteDot(), 3, 1);
        moved.Offset.Value = new Point(Pitch / 2f, 0);
        DrawableRepeater around = Repeat(WhiteDot(), 3, 1);
        around.Offset.Value = new Point(Pitch * 3, 0);
        DrawableRepeater backwards = Repeat(WhiteDot(), 3, 1);
        backwards.Offset.Value = new Point(-Pitch / 2f, 0);

        byte[,] movedGreens = Render(moved);
        byte[,] backwardsGreens = Render(backwards);

        Assert.Multiple(() =>
        {
            foreach (int x in new[] { -Pitch / 2, Pitch / 2, Pitch + Pitch / 2 })
                Assert.That(LitAt(movedGreens, x, 0), Is.True, $"at {x}");
            Assert.That(LitAt(movedGreens, -Pitch, 0), Is.False, "where the first was");

            Assert.That(Render(around), Is.EqualTo(Render(Repeat(WhiteDot(), 3, 1))), "once around the whole grid");

            // The first cell, half a step before the start, comes back after the last.
            foreach (int x in new[] { -Pitch / 2, Pitch / 2, Pitch + Pitch / 2 })
                Assert.That(LitAt(backwardsGreens, x, 0), Is.True, $"backwards at {x}");
        });
    }

    // A dot with its left half white, left of the middle of the cell when not mirrored.
    [Test]
    public void Mirroring_flips_every_other_column_and_row()
    {
        var half = new RectShape { Width = { Value = Dot }, Height = { Value = Dot } };
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Colors.White, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Black, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Black, 1));
        half.Fill.Value = gradient;
        half.Transform.Value = new TranslateTransform(0, Pitch / 4f);

        DrawableRepeater repeater = Repeat(half, 2, 2);
        repeater.Mirror.Value = DrawableRepeaterMirror.Both;

        byte[,] greens = Render(repeater);

        // Rows are half a pitch from the middle, and a mirrored row has the child above its cell's middle.
        int first = -Pitch / 2;
        int second = Pitch / 2;
        Assert.Multiple(() =>
        {
            Assert.That(LitAt(greens, first - 2, first + Pitch / 4), Is.True, "first column, left half");
            Assert.That(LitAt(greens, first + 2, first + Pitch / 4), Is.False, "first column, right half");
            Assert.That(LitAt(greens, second + 2, first + Pitch / 4), Is.True, "second column, mirrored to the right half");
            Assert.That(LitAt(greens, second - 2, first + Pitch / 4), Is.False, "second column, mirrored from the left half");
            Assert.That(LitAt(greens, first - 2, second - Pitch / 4), Is.True, "second row, mirrored up");
            Assert.That(LitAt(greens, first - 2, second + Pitch / 4), Is.False, "second row, not down");
        });
    }

    [Test]
    public void The_child_moves_inside_each_cell_and_the_transform_of_the_grid_moves_them_all()
    {
        RectShape child = WhiteDot();
        child.Transform.Value = new TranslateTransform(0, 10);
        DrawableRepeater repeater = Repeat(child, 3, 1);
        repeater.Transform.Value = new RotationTransform(90);

        byte[,] greens = Render(repeater);

        // The child's move takes the whole row down, and the row turns around its own middle into a column.
        Assert.Multiple(() =>
        {
            foreach (int y in new[] { 10 - Pitch, 10, 10 + Pitch })
                Assert.That(LitAt(greens, 0, y), Is.True, $"at {y}");
            Assert.That(LitAt(greens, Pitch, 10), Is.False, "where the row was");
        });
    }

    // Overlapping cells would otherwise show through each other where they cross.
    [Test]
    public void The_opacity_applies_to_the_grid_as_one_picture()
    {
        DrawableRepeater repeater = Repeat(WhiteDot(), 2, 1);
        repeater.Spacing.Value = new Size(Dot / 2f, Pitch);
        repeater.Opacity.Value = 50;

        byte[,] greens = Render(repeater);

        Assert.That(greens[Frame / 2, Frame / 2], Is.EqualTo(greens[Frame / 2, Frame / 2 - Dot / 2 - 1]).Within(1));
        Assert.That(greens[Frame / 2, Frame / 2], Is.InRange(150, 220));
    }

    [Test]
    public void Without_spacing_all_the_copies_stay_together()
    {
        DrawableRepeater repeater = Repeat(WhiteDot(), 3, 3);
        repeater.Spacing.Value = default;
        repeater.Offset.Value = new Point(5, 5);

        Assert.That(Render(repeater), Is.EqualTo(Render(WithOffset(WhiteDot(), 5))));
    }

    private static RectShape WithOffset(RectShape shape, float offset)
    {
        shape.Transform.Value = new TranslateTransform(offset, offset);
        return shape;
    }

    [Test]
    public void It_repeats_what_comes_before_it_in_the_clip()
    {
        using var harness = new SceneHistoryHarness("beutl_repeater_flow", duration: MediaTime.FromSeconds(10));
        Element element = harness.AddElement(MediaTime.Zero, MediaTime.FromSeconds(4));
        element.Objects.Add(WhiteDot());
        element.Objects.Add(new DrawableRepeater());
        using var compositor = new SceneCompositor(harness.Scene) { DisableResourceShare = true };

        var resource = (DrawableRepeater.Resource)compositor.EvaluateGraphics(MediaTime.FromSeconds(1)).Objects.Single();

        Assert.That(resource.Children.Single(), Is.InstanceOf<RectShape.Resource>());
    }

    [Test]
    public void It_is_saved_with_its_children_and_settings()
    {
        DrawableRepeater repeater = Repeat(WhiteDot(), 4, 2);
        repeater.Offset.Value = new Point(3, 5);
        repeater.Mirror.Value = DrawableRepeaterMirror.Rows;

        JsonObject json = CoreSerializer.SerializeToJsonObject(repeater);
        var restored = (DrawableRepeater)CoreSerializer.DeserializeFromJsonObject(json, typeof(DrawableRepeater));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Columns.Value, Is.EqualTo(4));
            Assert.That(restored.Rows.Value, Is.EqualTo(2));
            Assert.That(restored.Spacing.Value, Is.EqualTo(new Size(Pitch, Pitch)));
            Assert.That(restored.Offset.Value, Is.EqualTo(new Point(3, 5)));
            Assert.That(restored.Mirror.Value, Is.EqualTo(DrawableRepeaterMirror.Rows));
            Assert.That(restored.Children, Has.Count.EqualTo(1));
        });
    }
}
