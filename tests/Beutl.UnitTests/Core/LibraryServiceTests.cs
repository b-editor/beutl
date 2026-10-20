using Beutl.Services;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class LibraryServiceTests
{
    // A format of its own, so that nothing else reads the type while it is registered.
    private const string Format = "Beutl.UnitTests.SnapshotItem";

    [Test]
    public void GetTypesFromFormat_returns_a_copy_that_later_registrations_leave_unchanged()
    {
        IReadOnlySet<Type> before = LibraryService.Current.GetTypesFromFormat(Format);
        try
        {
            LibraryService.Current.Register<SnapshotItem>(Format, "Snapshot item");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(before, Is.Empty);
                Assert.That(LibraryService.Current.GetTypesFromFormat(Format), Is.EquivalentTo(new[] { typeof(SnapshotItem) }));
            }
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(SnapshotItem)]);
        }
    }

    private sealed class SnapshotItem;
}
