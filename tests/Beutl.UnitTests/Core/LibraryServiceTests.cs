using Beutl.Services;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class LibraryServiceTests
{
    private const string Format = "Beutl.UnitTests.SnapshotItem";

    [Test]
    public void GetTypesFromFormat_returns_a_copy_that_later_registrations_leave_unchanged()
    {
        using var library = new LibraryService();
        using var other = new LibraryService();
        IReadOnlySet<Type> before = library.GetTypesFromFormat(Format);
        library.Register<SnapshotItem>(Format, "Snapshot item");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Empty);
            Assert.That(library.GetTypesFromFormat(Format), Is.EquivalentTo(new[] { typeof(SnapshotItem) }));
            Assert.That(other.GetTypesFromFormat(Format), Is.Empty);
        }
    }

    [Test]
    public void A_group_registered_by_name_keeps_the_id_of_a_later_registration()
    {
        using var library = new LibraryService();
        const string id = "Beutl.UnitTests.Group";
        library.RegisterGroup("Group", group => group.Add<SnapshotItem>(Format, "First"));
        var first = (GroupLibraryItem)library.Items.Single();

        library.RegisterGroup("Group", null, id, group => group.Add<SnapshotItem>(Format, "Second"));
        library.RegisterGroup("Translated title", null, id, group => group.Add<SnapshotItem>(Format, "Third"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(library.Items.Single(), Is.SameAs(first));
            Assert.That(first.Id, Is.EqualTo(id));
            Assert.That(first.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "First", "Second", "Third" }));
        }
    }

    private sealed class SnapshotItem;
}
