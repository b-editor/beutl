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

    private sealed class SnapshotItem;
}
