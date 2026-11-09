using Beutl.Graphics;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class ShaderGraphSourceCropTests
{
    private static readonly Rect s_bounds = new(-11555.459f, -3329.1016f, 22556.04f, 12687.772f);

    // From a frame whose export failed: the copy asked for 16385 px on a device limited to 16384.
    private static readonly PixelRect s_target = new(-8394, -2419, 16384, 9216);
    private static readonly PixelRect s_footprint = new(-8395, -2419, 16385, 9216);

    [Test]
    public void A_partial_source_is_not_copied_for_rounding_alone()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, s_target, s_footprint, s_bounds, s_bounds), Is.False);
    }

    [Test]
    public void A_partial_source_on_its_footprint_is_used_as_it_is()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, s_footprint, s_footprint, s_bounds, s_bounds), Is.False);
    }

    [Test]
    public void A_partial_source_with_an_apron_is_cut_to_its_footprint()
    {
        var apron = new PixelRect(s_footprint.X - 1, s_footprint.Y - 1, s_footprint.Width + 2, s_footprint.Height + 2);

        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, apron, s_footprint, s_bounds, s_bounds), Is.True);
    }

    [Test]
    public void A_complete_source_still_has_to_match_its_footprint()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(false, s_target, s_footprint, s_bounds, s_bounds), Is.True);
    }

    [Test]
    public void A_part_of_a_picture_is_cut_for_a_graph_that_reads_the_whole()
    {
        var part = new Rect(s_bounds.X, s_bounds.Y, s_bounds.Width / 2, s_bounds.Height);

        Assert.That(RenderRequestExecutor.RequiresSourceCrop(false, s_footprint, s_footprint, part, s_bounds), Is.True);
    }
}
