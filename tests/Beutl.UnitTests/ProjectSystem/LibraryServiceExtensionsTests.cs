using Beutl.Audio;
using Beutl.Graphics.Shapes;
using Beutl.Services;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class LibraryServiceExtensionsTests
{
    // The timeline adds the items that are bound as an EngineObject.
    [Test]
    public void AddDrawable_BindsTheTypeAsADrawableAndAnEngineObject()
    {
        var group = new GroupLibraryItem("Group");

        group.AddDrawable<RectShape>("Rect", "A rectangle");

        var item = (MultipleTypeLibraryItem)group.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.DisplayName, Is.EqualTo("Rect"));
            Assert.That(item.Description, Is.EqualTo("A rectangle"));
            Assert.That(item.Types, Is.EquivalentTo(new Dictionary<string, Type>
            {
                [KnownLibraryItemFormats.Drawable] = typeof(RectShape),
                [KnownLibraryItemFormats.EngineObject] = typeof(RectShape),
            }));
        });
    }

    [Test]
    public void AddSound_BindsTheTypeAsASoundAndAnEngineObject()
    {
        var group = new GroupLibraryItem("Group");

        group.AddSound<SourceSound>("Sound", "A sound");

        var item = (MultipleTypeLibraryItem)group.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.DisplayName, Is.EqualTo("Sound"));
            Assert.That(item.Description, Is.EqualTo("A sound"));
            Assert.That(item.Types, Is.EquivalentTo(new Dictionary<string, Type>
            {
                [KnownLibraryItemFormats.Sound] = typeof(SourceSound),
                [KnownLibraryItemFormats.EngineObject] = typeof(SourceSound),
            }));
        });
    }
}
