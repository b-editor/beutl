using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Graphics;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class LutThumbnailRendererTests
{
    [Test]
    public void Identity_lut_leaves_the_reference_image_unchanged()
    {
        CubeFile cube = LutTestFiles.CreateCube();
        byte[] reference = LutThumbnailRenderer.CreateReferenceImage();
        byte[] rendered = LutThumbnailRenderer.CreateImage(cube);

        Assert.That(rendered, Is.EqualTo(reference));
    }

    [Test]
    public void One_dimensional_identity_lut_leaves_the_reference_image_unchanged()
    {
        CubeFile cube = LutTestFiles.CreateOneDimensionalCube();
        byte[] reference = LutThumbnailRenderer.CreateReferenceImage();
        byte[] rendered = LutThumbnailRenderer.CreateImage(cube);

        Assert.That(rendered, Is.EqualTo(reference));
    }

    [Test]
    public void Inverting_lut_inverts_the_reference_image_within_one_step()
    {
        CubeFile cube = LutTestFiles.CreateCube(invert: true);
        byte[] reference = LutThumbnailRenderer.CreateReferenceImage();
        byte[] rendered = LutThumbnailRenderer.CreateImage(cube);

        Assert.That(rendered, Has.Length.EqualTo(reference.Length));
        for (int i = 0; i < reference.Length; i += 4)
        {
            Assert.Multiple(() =>
            {
                Assert.That(rendered[i], Is.EqualTo(255 - reference[i]).Within(1), $"red at {i / 4}");
                Assert.That(rendered[i + 1], Is.EqualTo(255 - reference[i + 1]).Within(1), $"green at {i / 4}");
                Assert.That(rendered[i + 2], Is.EqualTo(255 - reference[i + 2]).Within(1), $"blue at {i / 4}");
                Assert.That(rendered[i + 3], Is.EqualTo(255), $"alpha at {i / 4}");
            });
        }
    }

    [Test]
    public void Reference_image_has_the_expected_size_and_covers_the_reference_colors()
    {
        byte[] reference = LutThumbnailRenderer.CreateReferenceImage();

        Assert.Multiple(() =>
        {
            Assert.That(reference, Has.Length.EqualTo(LutThumbnailRenderer.Width * LutThumbnailRenderer.Height * 4));
            Assert.That(reference.Where((_, index) => index % 4 == 3), Is.All.EqualTo(255));
        });
    }
}
