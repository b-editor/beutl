using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class RepeatEffectTests
{
    private const int Width = 200;
    private const int Height = 100;

    private static readonly Rect s_bounds = new(10, 20, 100, 60);

    // The picture is 40 x 20 in the middle of the frame: x from 80 to 120, y from 40 to 60.
    private static RectShape Picture(bool vertical = false)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(vertical ? 0.5f : 0, vertical ? 0 : 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(vertical ? 0.5f : 1, vertical ? 1 : 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var shape = new RectShape { Width = { Value = 40 }, Height = { Value = 20 } };
        shape.Fill.Value = gradient;
        return shape;
    }

    // BGRA bytes in sRGB without premultiplying, one array per row. Linear ones are premultiplied, so that a pixel that
    // differs by rounding moves as little near black or with next to no alpha as anywhere else.
    private static byte[][] Render(Drawable drawable, FilterEffect? effect, float scale = 1f, Rect? region = null, PixelSize? frame = null,
        bool linear = false)
    {
        byte[][] rows = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            drawable.FilterEffect.Value = effect;
            using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, frame ?? new PixelSize(Width, Height), scale, region, Colors.Transparent);
            using Bitmap bytes = linear
                ? rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb)
                : rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            rows = [.. Enumerable.Range(0, bytes.Height).Select(y => bytes.GetRow(y).ToArray())];
        });
        return rows;
    }

    private static char At(byte[][] rows, int x, int y)
    {
        byte[] row = rows[y];
        int blue = row[x * 4], red = row[x * 4 + 2], alpha = row[x * 4 + 3];
        return alpha < 8 ? '.' : red > 200 && blue < 55 ? 'R' : blue > 200 && red < 55 ? 'B' : '?';
    }

    // What is at each x along the row, one letter per position.
    private static string Across(byte[][] rows, int y, params int[] xs)
        => new([.. xs.Select(x => At(rows, x, y))]);

    private static string Down(byte[][] rows, int x, params int[] ys)
        => new([.. ys.Select(y => At(rows, x, y))]);

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [Test]
    public void The_copies_touch_and_are_centered_on_the_picture()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } });

        Assert.Multiple(() =>
        {
            Assert.That(Across(rows, 50, 35, 45, 59, 60, 79, 80, 99, 100, 119, 120, 139, 140, 159, 165), Is.EqualTo(".RRBBRRBBRRBB."));
            Assert.That(Down(rows, 100, 35, 45, 65), Is.EqualTo(".B."));
        });
    }

    [Test]
    public void The_spacing_leaves_a_gap_between_the_copies()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 }, Spacing = { Value = new Size(10, 0) } });

        // 40 wide with 10 between them: 30 to 70, 80 to 120 and 130 to 170.
        Assert.That(Across(rows, 50, 25, 35, 65, 75, 85, 115, 125, 135, 165, 175), Is.EqualTo(".RB.RB.RB."));
    }

    [Test]
    public void The_rows_repeat_too()
    {
        byte[][] rows = Render(Picture(vertical: true), new RepeatEffect { Columns = { Value = 1 }, Rows = { Value = 2 }, Spacing = { Value = new Size(0, 10) } });

        // 20 tall with 10 between them: 25 to 45 and 55 to 75.
        Assert.That(Down(rows, 100, 20, 30, 40, 50, 60, 70, 80), Is.EqualTo(".RB.RB."));
    }

    [Test]
    public void The_offset_moves_the_copies_and_what_leaves_one_end_comes_back_at_the_other()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect { Columns = { Value = 2 }, Rows = { Value = 1 }, Offset = { Value = new Point(10, 0) } });

        // Still 60 to 140, with the last 10 of the second copy at the start.
        Assert.That(Across(rows, 50, 55, 65, 75, 95, 115, 135, 145), Is.EqualTo(".BRBRB."));
    }

    [Test]
    public void The_offset_wraps_around_in_the_other_direction_and_past_the_whole_grid()
    {
        byte[][] back = Render(Picture(), new RepeatEffect { Columns = { Value = 2 }, Rows = { Value = 1 }, Offset = { Value = new Point(-10, 0) } });
        byte[][] around = Render(Picture(), new RepeatEffect { Columns = { Value = 2 }, Rows = { Value = 1 }, Offset = { Value = new Point(170, 0) } });

        Assert.Multiple(() =>
        {
            // 60 to 70 is the first copy's red, moved in from 70.
            Assert.That(Across(back, 50, 65, 75, 95, 115, 135), Is.EqualTo("RBRBR"));
            // Two copies are 80 wide, so 170 moves as far as 10.
            Assert.That(Across(around, 50, 65, 75, 95, 115, 135), Is.EqualTo("BRBRB"));
        });
    }

    [Test]
    public void One_copy_with_an_offset_scrolls_inside_the_picture()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect { Columns = { Value = 1 }, Rows = { Value = 1 }, Offset = { Value = new Point(10, 0) } });

        Assert.That(Across(rows, 50, 75, 85, 95, 115, 125), Is.EqualTo(".BRB."));
    }

    [TestCase(RepeatEffectMirror.None, "RBRB", "RBRB")]
    [TestCase(RepeatEffectMirror.Columns, "RBBR", "RBRB")]
    [TestCase(RepeatEffectMirror.Rows, "RBRB", "RBBR")]
    [TestCase(RepeatEffectMirror.Both, "RBBR", "RBBR")]
    public void Mirroring_flips_every_other_copy(RepeatEffectMirror mirror, string across, string down)
    {
        byte[][] wide = Render(Picture(), new RepeatEffect { Columns = { Value = 2 }, Rows = { Value = 1 }, Mirror = { Value = mirror } });
        byte[][] tall = Render(Picture(vertical: true), new RepeatEffect { Columns = { Value = 1 }, Rows = { Value = 2 }, Mirror = { Value = mirror } });

        Assert.Multiple(() =>
        {
            Assert.That(Across(wide, 50, 65, 85, 105, 125), Is.EqualTo(across));
            Assert.That(Down(tall, 100, 35, 45, 55, 65), Is.EqualTo(down));
        });
    }

    // Flipped neighbours meet at the same edge, which moving them must not undo. An odd number shows it best.
    [Test]
    public void Flipped_copies_keep_alternating_when_the_offset_moves_them()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect
        {
            Columns = { Value = 3 },
            Rows = { Value = 1 },
            Offset = { Value = new Point(40, 0) },
            Mirror = { Value = RepeatEffectMirror.Columns },
        });

        Assert.That(Across(rows, 50, 50, 70, 90, 110, 130, 150), Is.EqualTo("BRRBBR"));
    }

    // An animated offset spends most of its time between pixels. The gap between rows must not change that.
    [TestCase(RepeatEffectMirror.None)]
    [TestCase(RepeatEffectMirror.Columns)]
    public void Copies_that_touch_show_no_seam_between_pixels(RepeatEffectMirror mirror)
    {
        byte[][] rows = Render(Picture(), new RepeatEffect
        {
            Columns = { Value = 3 },
            Rows = { Value = 1 },
            Spacing = { Value = new Size(0, 10) },
            Offset = { Value = new Point(0.5f, 0) },
            Mirror = { Value = mirror },
        });

        int[] alphas = [.. Enumerable.Range(40, 120).Select(x => (int)rows[50][x * 4 + 3])];
        Assert.That(alphas, Is.All.EqualTo(255));
    }

    // 99.25 to 100.75 fills most of a red texel and most of a blue one, so no texel is filled completely to stay on.
    [Test]
    public void A_picture_with_no_filled_texel_still_reads_all_of_itself()
    {
        RectShape picture = Picture();
        picture.Width.Value = 1.5f;

        byte[][] rows = Render(picture, new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 }, Offset = { Value = new Point(0.25f, 0) } });

        byte[] pixel = rows[50][(98 * 4)..(99 * 4)];
        Assert.Multiple(() =>
        {
            Assert.That((int)pixel[2], Is.GreaterThan(100), "red");
            Assert.That((int)pixel[0], Is.GreaterThan(100), "blue");
            Assert.That((int)pixel[3], Is.InRange(180, 200), "alpha");
        });
    }

    // The texels at the ends of such a picture are only partly filled, so reading them would show a line at every join.
    [TestCase(0.66f, 40f)]
    [TestCase(1f, 41f)]
    public void Copies_that_touch_show_no_seam_when_the_picture_ends_between_pixels(float scale, float width)
    {
        RectShape picture = Picture();
        picture.Width.Value = width;

        byte[][] rows = Render(picture, new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } }, scale);

        int left = (int)MathF.Ceiling((100 - 1.5f * width) * scale);
        int right = (int)MathF.Floor((100 + 1.5f * width) * scale);
        int[] alphas = [.. Enumerable.Range(left, right - left).Select(x => (int)rows[(int)(50 * scale)][x * 4 + 3])];
        Assert.That(alphas, Is.All.EqualTo(255));
    }

    [Test]
    public void Copies_next_to_a_gap_fade_at_their_edges_between_pixels()
    {
        byte[][] rows = Render(Picture(), new RepeatEffect
        {
            Columns = { Value = 2 },
            Rows = { Value = 1 },
            Spacing = { Value = new Size(10, 0) },
            Offset = { Value = new Point(0.75f, 0) },
        });

        // 55.75 to 95.75 and 105.75 to 145.75, so the pixels that hold those edges are a quarter off their centers.
        int Alpha(int x) => rows[50][x * 4 + 3];
        Assert.Multiple(() =>
        {
            Assert.That(Alpha(94), Is.EqualTo(255));
            Assert.That(Alpha(95), Is.InRange(185, 197));
            Assert.That(Alpha(96), Is.Zero);
            Assert.That(Alpha(104), Is.Zero);
            Assert.That(Alpha(105), Is.InRange(58, 70));
            Assert.That(Alpha(106), Is.EqualTo(255));
        });
    }

    // An effect before it can leave the picture between pixels, so the image behind the grid reaches past the bounds.
    [Test]
    public void The_next_copy_does_not_show_past_the_bounds()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new TransformEffect { Transform = { Value = new TranslateTransform(new Point(0, 0.3f)) } });
        group.Children.Add(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } });

        byte[][] rows = Render(Picture(vertical: true), group);

        // 40.3 to 60.3, so the center of row 60 is past the end.
        Assert.That(Down(rows, 100, 39, 41, 59, 60, 61), Is.EqualTo(".RB.."));
    }

    [Test]
    public void One_copy_that_stays_in_place_records_nothing()
    {
        using var resource = new RepeatEffect.Resource { Columns = 1, Rows = 1, Spacing = new Size(50, 50) };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void The_copies_follow_the_output_scale(float scale)
    {
        byte[][] rows = Render(Picture(), new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 }, Spacing = { Value = new Size(10, 0) } }, scale);

        int X(int x) => (int)(x * scale);
        Assert.That(Across(rows, X(50), X(25), X(35), X(65), X(75), X(85), X(115), X(125), X(135), X(165), X(175)), Is.EqualTo(".RB.RB.RB."));
    }

    // The part goes through the object's transform like the whole, on whatever grid of pixels that puts it.
    private static IEnumerable<TestCaseData> Transforms()
    {
        yield return new TestCaseData(new TranslateTransform(new Point(0.25f, 0.5f))).SetName("BetweenPixels");
        yield return new TestCaseData(new RotationTransform(15)).SetName("Rotated");
        yield return new TestCaseData(new ScaleTransform(new Vector(150, 150))).SetName("Scaled");
    }

    [TestCaseSource(nameof(Transforms))]
    public void Drawing_part_of_a_grid_under_a_transform_gives_the_same_pixels(Transform transform)
    {
        RectShape Moved()
        {
            RectShape picture = Picture();
            picture.Width.Value = 120;
            picture.Height.Value = 80;
            picture.Transform.Value = transform;
            return picture;
        }

        var frame = new PixelSize(1920, 1880);
        Rect region = new(300, 500, 700, 500);
        byte[][] whole = Render(Moved(), WideGrid(10), frame: frame, linear: true);
        byte[][] part = Render(Moved(), WideGrid(10), region: region, frame: frame, linear: true);

        // Up to its edges: the outermost pixels of the part read past what was asked for, as the whole's do.
        (int Difference, int X, int Y) largest = default;
        for (int y = 500; y < 1000; y++)
            for (int x = 300 * 4; x < 1000 * 4; x++)
            {
                int difference = Math.Abs(part[y][x] - whole[y][x]);
                if (difference > largest.Difference)
                    largest = (difference, x / 4, y);
            }
        Assert.That(largest.Difference, Is.LessThanOrEqualTo(2), $"at ({largest.X}, {largest.Y})");
    }

    // Past 16384 pixels no single image could hold the whole grid, which leaves nothing to compare with except the pattern.
    [Test]
    public void Drawing_part_of_a_wide_grid_at_another_scale_gives_the_same_pattern()
    {
        RectShape picture = Picture();
        picture.Width.Value = 120;
        picture.Height.Value = 80;

        byte[][] rows = Render(picture, WideGrid(100), 2, frame: new PixelSize(1920, 1080));

        Assert.That(Across(rows, 620 * 2, 2000, 2118, 2119, 2120, 2121, 2238, 2239, 2240), Is.EqualTo("RRRBBBB."));
    }

    // Any pixel of the grid can read any of the picture, which is outside the part being drawn here.
    [Test]
    public void Drawing_part_of_the_grid_gives_the_same_pixels()
    {
        var effect = new RepeatEffect { Columns = { Value = 5 }, Rows = { Value = 1 } };
        byte[][] whole = Render(Picture(), effect);

        byte[][] part = Render(Picture(), effect, region: new Rect(10, 40, 30, 20));

        Assert.Multiple(() =>
        {
            Assert.That(Across(part, 50, 15, 25), Is.EqualTo("RB"));
            Assert.That(part[50][(10 * 4)..(40 * 4)], Is.EqualTo(whole[50][(10 * 4)..(40 * 4)]));
        });
    }

    // The two halves of a split picture repeat as the one picture they came from, each reaching every copy.
    [Test]
    public void A_split_picture_repeats_as_one()
    {
        var repeat = new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } };
        byte[][] plain = Render(Picture(), repeat);
        var group = new FilterEffectGroup();
        group.Children.Add(new SplitEffect { HorizontalDivisions = { Value = 2 }, VerticalDivisions = { Value = 1 } });
        group.Children.Add(repeat);

        byte[][] rows = Render(Picture(), group);

        int[] xs = [35, 45, 59, 60, 79, 80, 99, 100, 119, 120, 139, 140, 159, 165];
        Assert.That(Across(rows, 50, xs), Is.EqualTo(Across(plain, 50, xs)));
    }

    [Test]
    public void A_split_picture_shows_no_seam_between_pixels()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new SplitEffect { HorizontalDivisions = { Value = 2 }, VerticalDivisions = { Value = 1 } });
        group.Children.Add(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 }, Offset = { Value = new Point(0.5f, 0) } });

        byte[][] rows = Render(Picture(), group);

        int[] alphas = [.. Enumerable.Range(40, 120).Select(x => (int)rows[50][x * 4 + 3])];
        Assert.That(alphas, Is.All.EqualTo(255));
    }

    // Where two pieces meet, each pixel belongs to one of them. Both drawing it would darken a line through a see-through picture.
    [TestCase(2, 0.5f)]
    [TestCase(3, 0f)]
    public void A_split_see_through_picture_stays_even(int divisions, float offset)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(128, 255, 0, 0), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(128, 255, 0, 0), 1));
        RectShape picture = Picture();
        picture.Fill.Value = gradient;
        var group = new FilterEffectGroup();
        group.Children.Add(new SplitEffect { HorizontalDivisions = { Value = divisions }, VerticalDivisions = { Value = 1 } });
        group.Children.Add(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 }, Offset = { Value = new Point(offset, 0) } });

        byte[][] rows = Render(picture, group);

        int[] alphas = [.. Enumerable.Range(40, 120).Select(x => (int)rows[50][x * 4 + 3])];
        Assert.That(alphas, Is.All.InRange(118, 138));
    }

    // However many the grid has, a 1920 x 1080 frame shows ten columns of these: 120 x 80, one every 200. The row at
    // y = 620 goes through a copy, and along it a copy starts every 200 from x = 0.
    private static byte[][] RenderInFrame(FilterEffect effect, Drawable? drawable = null, Rect? region = null, bool linear = false)
    {
        if (drawable is null)
        {
            RectShape picture = Picture();
            picture.Width.Value = 120;
            picture.Height.Value = 80;
            drawable = picture;
        }

        return Render(drawable, effect, region: region, frame: new PixelSize(1920, 1080), linear: linear);
    }

    // The picture RenderInFrame draws, as the two children of a group: red on the left and blue on the right. A graph
    // effect gets them as separate pieces.
    private static DrawableGroup Halves()
    {
        var group = new DrawableGroup();
        foreach ((Color color, float x) in new[] { (Colors.Red, -30f), (Colors.Blue, 30f) })
        {
            var half = new RectShape { Width = { Value = 60 }, Height = { Value = 80 } };
            half.Fill.Value = new SolidColorBrush(color);
            half.Transform.Value = new TranslateTransform(x, 0);
            group.Children.Add(half);
        }

        return group;
    }

    private static readonly string s_frameRow =
        new([.. Enumerable.Range(0, 1920).Select(static x => x % 200 < 60 ? 'R' : x % 200 < 120 ? 'B' : '.')]);

    private static string FrameRow(byte[][] rows) => Across(rows, 620, [.. Enumerable.Range(0, 1920)]);

    private static RepeatEffect WideGrid(int count) => new()
    {
        Columns = { Value = count },
        Rows = { Value = count },
        Spacing = { Value = new Size(80, 120) },
    };

    [TestCase(10)]
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(82)]
    [TestCase(100)]
    public void A_wide_grid_stays_sharp_in_the_frame(int count)
    {
        byte[][] rows = RenderInFrame(WideGrid(count));

        Assert.That(FrameRow(rows), Is.EqualTo(s_frameRow));
    }

    // The pieces repeat around the group's middle as the one picture they make.
    [TestCase(10)]
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(100)]
    public void A_wide_grid_of_a_group_stays_sharp_in_the_frame(int count)
    {
        byte[][] rows = RenderInFrame(WideGrid(count), Halves());

        Assert.That(FrameRow(rows), Is.EqualTo(s_frameRow));
    }

    [Test]
    public void Drawing_part_of_a_group_s_grid_gives_the_same_pixels()
    {
        var region = new PixelRect(300, 500, 700, 300);
        byte[][] whole = RenderInFrame(WideGrid(10), Halves(), linear: true);

        byte[][] part = RenderInFrame(WideGrid(10), Halves(), region.ToRect(1), linear: true);

        // The part's images have other sizes than the whole's. SwiftShader rounds the positions a copy samples at
        // differently with them, which can move a pixel by a step.
        (int Difference, int X, int Y) largest = default;
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X * 4; x < region.Right * 4; x++)
            {
                int difference = Math.Abs(part[y][x] - whole[y][x]);
                if (difference > largest.Difference)
                    largest = (difference, x / 4, y);
            }
        Assert.Multiple(() =>
        {
            Assert.That(Across(part, 620, 459, 460, 519, 520), Is.EqualTo("RBB."));
            Assert.That(largest.Difference, Is.LessThanOrEqualTo(2), $"at ({largest.X}, {largest.Y})");
        });
    }

    // The effects one effect records run in one segment, where the grid makes only what the filter after it reads.
    [TestCase(30)]
    [TestCase(100)]
    public void A_wide_grid_of_a_group_stays_sharp_under_a_filter_in_the_same_effect(int count)
    {
        using FilterEffect.Resource grid = WideGrid(count).ToResource(CompositionContext.Default);
        using FilterEffect.Resource shadow = new DropShadow
        {
            Position = { Value = new Point(10, 10) },
            Sigma = { Value = new Size(4, 4) },
            Color = { Value = Colors.Black },
        }.ToResource(CompositionContext.Default);

        byte[][] rows = RenderInFrame(new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            context.Apply(grid);
            context.Apply(shadow);
        }), Halves());

        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119), Is.EqualTo("RRBB"));
    }

    // A tiled crop reads only its rectangle but fills all of what it gets, so the grid before it is made whole. The
    // cell it keeps is one period of the grid, so the tiles give back the grid's own pattern.
    [Test]
    public void A_tiled_crop_after_a_group_s_grid_in_the_same_effect_still_fills_the_frame()
    {
        using FilterEffect.Resource grid = WideGrid(10).ToResource(CompositionContext.Default);

        byte[][] rows = RenderInFrame(new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            context.Apply(grid);
            Rect bounds = context.Bounds;
            context.Crop(new Rect(bounds.X + 800, bounds.Y + 1000, 200, 200), GradientSpreadMethod.Repeat);
        }), Halves());

        Assert.That(FrameRow(rows), Is.EqualTo(s_frameRow));
    }

    // A tiling crop draws a group's children into one before it, and two children that each fit a buffer can be too wide
    // together. The merge lowers its density rather than dropping the group, as a flush does (#4088). Each child is
    // 0.6 of the device's limit wide, which is 8192 px on CI's SwiftShader and 16384 px on MoltenVK.
    [Test]
    public void A_tiled_crop_over_a_group_wider_than_a_buffer_still_fills_the_frame()
    {
        int width = VulkanTestEnvironment.InvokeOnRenderThread(
            static () => BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation).MaxDimension * 6 / 10);
        var group = new DrawableGroup();
        foreach (float x in new[] { -width / 2f, width / 2f })
        {
            var half = new RectShape { Width = { Value = width }, Height = { Value = 100 } };
            half.Fill.Value = new SolidColorBrush(Colors.Red);
            half.Transform.Value = new TranslateTransform(x, 0);
            group.Children.Add(half);
        }

        byte[][] rows = RenderInFrame(new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            Rect bounds = context.Bounds;
            context.Crop(new Rect(bounds.X, bounds.Y, 50, 50), GradientSpreadMethod.Repeat);
        }), group);

        Assert.That(Across(rows, 540, 0, 960, 1919), Is.EqualTo("RRR"));
    }

    // An effect whose bounds are known only when it runs keeps the effects after it in its segment.
    [TestCase(30)]
    [TestCase(100)]
    public void A_wide_grid_after_an_effect_of_unknown_bounds_stays_sharp_in_the_frame(int count)
    {
        using FilterEffect.Resource grid = WideGrid(count).ToResource(CompositionContext.Default);

        byte[][] rows = RenderInFrame(new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            context.CustomEffect(0, static (_, _) => { }, static (_, _) => Rect.Invalid);
            context.Apply(grid);
        }));

        Assert.That(FrameRow(rows), Is.EqualTo(s_frameRow));
    }

    // An effect that reaches the composition target runs with the effects after it as work on the target.
    [TestCase(30)]
    [TestCase(100)]
    public void A_wide_grid_after_an_effect_on_the_target_stays_sharp_in_the_frame(int count)
    {
        using FilterEffect.Resource grid = WideGrid(count).ToResource(CompositionContext.Default);

        byte[][] rows = RenderInFrame(new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            context.TargetEffect(0, static (_, _, _) => { });
            context.Apply(grid);
        }, declaresTargetAccess: true));

        Assert.That(FrameRow(rows), Is.EqualTo(s_frameRow));
    }

    private static DropShadow Shadow() => new()
    {
        Position = { Value = new Point(10, 10) },
        Sigma = { Value = new Size(4, 4) },
        Color = { Value = Colors.Black },
    };

    // A Skia filter after the grid makes only what the frame shows, so 16384 pixels across the whole grid set no limit.
    [TestCase(30)]
    [TestCase(82)]
    [TestCase(100)]
    public void A_wide_grid_stays_sharp_under_a_filter_after_it(int count)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(WideGrid(count));
        group.Children.Add(Shadow());

        byte[][] rows = RenderInFrame(group);

        // The shadow falls in the gaps, and the copies stay as they are over it.
        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119), Is.EqualTo("RRBB"));
    }

    // Several pictures reach the filter, which then runs as an effect over all of them.
    [TestCase(false)]
    [TestCase(true)]
    public void A_wide_grid_of_pieces_stays_sharp_under_a_filter_after_it(bool group)
    {
        var effects = new FilterEffectGroup();
        if (!group)
            effects.Children.Add(new SplitEffect { HorizontalDivisions = { Value = 2 }, VerticalDivisions = { Value = 1 } });
        effects.Children.Add(WideGrid(100));
        effects.Children.Add(Shadow());

        byte[][] rows = RenderInFrame(effects, group ? Halves() : null);

        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119), Is.EqualTo("RRBB"));
    }

    // Scaled up, the copies do not land on whole pixels of the frame as they are drawn, so the filter makes a value of
    // what the frame shows instead of drawing straight into it. A grid too wide for one buffer then gives the pixels of
    // one that fits.
    [Test]
    public void A_wide_grid_scaled_up_under_a_filter_after_it_draws_like_a_small_one()
    {
        byte[][] small = RenderScaledUp(10);

        byte[][] wide = RenderScaledUp(100);

        // Twice as large around the picture's middle, the copy at x = 1000 covers 1040 to 1280, and the rows of copies
        // from y = 600 are from 660.
        (int Difference, int X, int Y) largest = default;
        for (int y = 600; y < 900; y++)
            for (int x = 900 * 4; x < 1500 * 4; x++)
            {
                int difference = Math.Abs(wide[y][x] - small[y][x]);
                if (difference > largest.Difference)
                    largest = (difference, x / 4, y);
            }
        Assert.That(largest.Difference, Is.LessThanOrEqualTo(2), $"at ({largest.X}, {largest.Y})");

        static byte[][] RenderScaledUp(int count)
        {
            RectShape picture = Picture();
            picture.Width.Value = 120;
            picture.Height.Value = 80;
            picture.Transform.Value = new ScaleTransform(new Vector(100, 100), 200);
            var effects = new FilterEffectGroup();
            effects.Children.Add(WideGrid(count));
            effects.Children.Add(Shadow());
            return RenderInFrame(effects, picture, linear: true);
        }
    }

    // Once the cache keeps the filter, it makes the filter's value instead of drawing it straight into the frame. That
    // value holds what the frame shows, at the density the cache's entry names, and comes back on the next frame. A node
    // of its own makes the picture, so that no transform above the filter keeps the cache away.
    [Test]
    public void A_wide_grid_stays_sharp_under_a_cached_filter_after_it()
    {
        var frame = new Rect(0, 0, 1920, 1080);
        bool cached = false;
        List<string> rows = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffect.Resource grid = WideGrid(100).ToResource(CompositionContext.Default);
            using FilterEffect.Resource shadow = Shadow().ToResource(CompositionContext.Default);
            var gridNode = new FilterEffectRenderNode(grid);
            gridNode.AddChild(new PictureSourceNode());
            using var shadowNode = new FilterEffectRenderNode(shadow);
            shadowNode.AddChild(gridNode);
            using var renderer = new RenderNodeRenderer(shadowNode, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                Purpose = RenderRequestPurpose.Frame,
                TargetDomain = frame,
                OutputScale = 1,
                // The default rules keep nothing larger than a million pixels.
                CacheOptions = new RenderCacheOptions(true, RenderCacheRules.Create(1920 * 1080, 1)),
            });

            var result = new List<string>();
            for (int index = 0; index < 3; index++)
            {
                using RenderNodeRasterization raster = renderer.Rasterize();
                Assert.That(raster.Bounds, Is.EqualTo(frame));
                using Bitmap bytes = raster.Bitmap!.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                byte[][] frameRows = [.. Enumerable.Range(0, bytes.Height).Select(y => bytes.GetRow(y).ToArray())];
                result.Add(Across(frameRows, 620, 1000, 1059, 1060, 1119));
                // The next frame stores the filter's value, and the last one takes it back.
                if (index == 0)
                    shadowNode.Cache.RecordStableRequests();
            }

            cached = shadowNode.Cache.IsCached;
            return result;
        });

        Assert.Multiple(() =>
        {
            Assert.That(cached, Is.True, "the filter has to reach the cache");
            Assert.That(rows, Is.All.EqualTo("RRBB"));
        });
    }

    // The picture RenderInFrame draws: red from x = 900 to 960 and blue to 1020, from y = 500 to 580.
    private sealed class PictureSourceNode : RenderNode
    {
        private static readonly Rect s_picture = new(900, 500, 120, 80);

        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                "red-and-blue",
                static (session, _) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(s_picture);
                    output.Canvas.Use(static canvas =>
                    {
                        using (canvas.PushClip(new Rect(900, 500, 60, 80)))
                            canvas.Clear(Colors.Red);
                        using (canvas.PushClip(new Rect(960, 500, 60, 80)))
                            canvas.Clear(Colors.Blue);
                    });
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_picture),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
    }

    [TestCase(30)]
    [TestCase(100)]
    public void A_wide_grid_stays_sharp_under_a_shader_graph_after_it(int count)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(WideGrid(count));
        group.Children.Add(new Sharpen { Amount = { Value = 10 }, Radius = { Value = 2 } });

        byte[][] rows = RenderInFrame(group);

        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119, 1200, 1259, 1260, 1319), Is.EqualTo("RRBBRRBB"));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(100)]
    public void A_wide_grid_of_a_split_picture_stays_sharp_under_a_shader_graph_after_it(int count)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new SplitEffect { HorizontalDivisions = { Value = 2 }, VerticalDivisions = { Value = 1 } });
        group.Children.Add(WideGrid(count));
        group.Children.Add(new Sharpen { Amount = { Value = 10 }, Radius = { Value = 2 } });

        byte[][] rows = RenderInFrame(group);

        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119, 1200, 1259, 1260, 1319), Is.EqualTo("RRBBRRBB"));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(100)]
    public void A_wide_grid_of_a_group_stays_sharp_under_a_shader_graph_after_it(int count)
    {
        var group = new FilterEffectGroup();
        group.Children.Add(WideGrid(count));
        group.Children.Add(new Sharpen { Amount = { Value = 10 }, Radius = { Value = 2 } });

        byte[][] rows = RenderInFrame(group, Halves());

        Assert.That(Across(rows, 620, 1000, 1059, 1060, 1119, 1200, 1259, 1260, 1319), Is.EqualTo("RRBBRRBB"));
    }

    // The graph runs over each piece's grid, and sharpens only where red meets blue within a piece. The region starts and
    // ends 2 pixels past such a meeting, so a part that leaves out what the graph reads around those pixels differs.
    [TestCase(false, 262)]
    [TestCase(true, 232)]
    public void Drawing_part_of_a_grid_of_pieces_under_a_shader_graph_gives_the_same_pixels(bool group, int left)
    {
        var region = new PixelRect(left, 500, 600, 300);
        byte[][] whole = RenderPiecesUnderAGraph(group, null);

        byte[][] part = RenderPiecesUnderAGraph(group, region.ToRect(1));

        (int Difference, int X, int Y) largest = default;
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X * 4; x < region.Right * 4; x++)
            {
                int difference = Math.Abs(part[y][x] - whole[y][x]);
                if (difference > largest.Difference)
                    largest = (difference, x / 4, y);
            }
        Assert.That(largest.Difference, Is.LessThanOrEqualTo(2), $"at ({largest.X}, {largest.Y})");
    }

    // The split cuts the picture in three, so the middle piece has red and blue. The group has two pictures that are each
    // red on the left and blue on the right.
    private static byte[][] RenderPiecesUnderAGraph(bool group, Rect? region)
    {
        var effects = new FilterEffectGroup();
        DrawableGroup? pieces = null;
        if (group)
        {
            pieces = new DrawableGroup();
            foreach (float x in new[] { -30f, 30f })
            {
                RectShape piece = Picture();
                piece.Width.Value = 60;
                piece.Height.Value = 80;
                piece.Transform.Value = new TranslateTransform(x, 0);
                pieces.Children.Add(piece);
            }
        }
        else
        {
            effects.Children.Add(new SplitEffect { HorizontalDivisions = { Value = 3 }, VerticalDivisions = { Value = 1 } });
        }

        effects.Children.Add(WideGrid(10));
        effects.Children.Add(new Sharpen { Amount = { Value = 100 }, Radius = { Value = 4 } });
        return RenderInFrame(effects, pieces, region, linear: true);
    }

    // A full frame of red and blue, which meet in the middle of each copy. An even count puts a join in the middle of
    // the frame, so half of the frame is the blue end of one copy and the rest the red start of the next.
    [TestCase(4)]
    [TestCase(10)]
    public void A_picture_as_large_as_the_frame_stays_sharp_up_to_its_edges(int count)
    {
        RectShape picture = Picture();
        picture.Width.Value = 1920;
        picture.Height.Value = 1080;

        byte[][] rows = Render(picture, new RepeatEffect { Columns = { Value = count }, Rows = { Value = count } }, frame: new PixelSize(1920, 1080));

        int[] alphas = [.. Enumerable.Range(0, 1920).Select(x => (int)rows[270][x * 4 + 3])];
        Assert.Multiple(() =>
        {
            Assert.That(Across(rows, 270, 0, 1, 958, 959, 960, 961, 1918, 1919), Is.EqualTo("BBBBRRRR"));
            Assert.That(alphas, Is.All.EqualTo(255));
        });
    }

    [Test]
    public void An_effect_before_it_is_repeated_with_the_picture()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new TransformEffect { Transform = { Value = new RotationTransform(90) } });
        group.Children.Add(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } });

        byte[][] rows = Render(Picture(), group);

        // Each copy is on its side, 20 wide and 40 tall with red on top: 70 to 130 across, 30 to 70 down.
        Assert.Multiple(() =>
        {
            Assert.That(Across(rows, 40, 65, 75, 95, 125, 135), Is.EqualTo(".RRR."));
            Assert.That(Across(rows, 60, 65, 75, 95, 125, 135), Is.EqualTo(".BBB."));
        });
    }

    [Test]
    public void An_effect_after_it_takes_the_grid_as_one_picture()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new RepeatEffect { Columns = { Value = 3 }, Rows = { Value = 1 } });
        group.Children.Add(new TransformEffect { Transform = { Value = new RotationTransform(90) } });

        byte[][] rows = Render(Picture(), group);

        // The grid of 120 x 20 is on its side as a whole: one column from 90 to 110, with each copy 40 tall.
        Assert.Multiple(() =>
        {
            Assert.That(Across(rows, 45, 85, 95, 105, 115), Is.EqualTo(".RR."));
            Assert.That(Down(rows, 100, 5, 15, 25, 35, 45, 55, 65, 75, 85, 95), Is.EqualTo("RBBRRBBRRB"));
        });
    }

    [Test]
    public void The_bounds_cover_the_copies_and_the_gaps_and_do_not_follow_the_offset()
    {
        Rect Grid(Point offset)
        {
            using var resource = new RepeatEffect.Resource { Columns = 3, Rows = 2, Spacing = new Size(10, 5), Offset = offset };
            using var context = new FilterEffectContext(s_bounds);
            context.ApplyTransactional(resource);
            return context.Bounds;
        }

        Assert.Multiple(() =>
        {
            Assert.That(Grid(default), Is.EqualTo(new Rect(-100, -12.5f, 320, 125)));
            Assert.That(Grid(new Point(37, -12)), Is.EqualTo(new Rect(-100, -12.5f, 320, 125)));
        });
    }

    [Test]
    public void Counts_and_spacing_stay_in_range()
    {
        var effect = new RepeatEffect { Columns = { Value = 0 }, Rows = { Value = 1000 }, Spacing = { Value = new Size(-10, -10) } };

        Assert.Multiple(() =>
        {
            Assert.That(effect.Columns.Value, Is.EqualTo(1));
            Assert.That(effect.Rows.Value, Is.EqualTo(100));
            Assert.That(effect.Spacing.Value, Is.EqualTo(new Size(0, 0)));
        });
    }

    [TestCase(float.NaN, 0f, 0f, 0f)]
    [TestCase(0f, float.PositiveInfinity, 0f, 0f)]
    [TestCase(0f, 0f, float.NaN, 0f)]
    [TestCase(0f, 0f, 0f, float.NegativeInfinity)]
    public void A_value_that_is_not_finite_records_nothing(float spacingX, float spacingY, float offsetX, float offsetY)
    {
        using var resource = new RepeatEffect.Resource
        {
            Columns = 3,
            Rows = 3,
            Spacing = new Size(spacingX, spacingY),
            Offset = new Point(offsetX, offsetY),
        };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });
    }
}
