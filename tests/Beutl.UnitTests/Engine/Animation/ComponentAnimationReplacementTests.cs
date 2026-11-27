using System.Numerics;
using Beutl.Animation;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Animation;

public class ComponentAnimationReplacementTests
{
    [TestCase(0b0000, 1f, 2f, 3f, 4f)]
    [TestCase(0b0101, 10f, 2f, 30f, 4f)]
    [TestCase(0b1111, 10f, 20f, 30f, 40f)]
    [TestCase(0b10010, 1f, 20f, 3f, 4f)]
    public void Masked_replacement_uses_only_marked_components(int mask, float x, float y, float z, float w)
    {
        var value = new Vector4(1, 2, 3, 4);
        var input = new Vector4(10, 20, 30, 40);

        Assert.That(ComponentAnimations.ReplaceEditedComponents(value, input, mask), Is.EqualTo(new Vector4(x, y, z, w)));
    }

    [Test]
    public void A_unit_change_with_an_empty_mask_keeps_all_numeric_components()
    {
        var value = new RelativeRect(10, 20, 30, 40, RelativeUnit.Absolute);
        var input = new RelativeRect(0.1f, 0.2f, 0.3f, 0.4f, RelativeUnit.Relative);

        var result = ComponentAnimations.ReplaceEditedComponents(value, input, editedComponents: 0);

        Assert.That(result, Is.EqualTo(new RelativeRect(value.Rect, RelativeUnit.Relative)));
    }
}
