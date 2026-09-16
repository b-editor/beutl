using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

// The search for a Windows command's live descendants when it has no job, and the Windows interop
// layouts. Both are platform neutral, so these run everywhere.
[TestFixture]
public class ProcessTreeWalkTests
{
    private const int RootId = 10;
    private const long RootCreationTime = 100;

    [Test]
    public void Finds_a_live_child()
    {
        Assert.That(HasLiveDescendant(Entry(11, RootId, 110)), Is.True);
    }

    [Test]
    public void Finds_a_live_process_below_an_exited_one_that_is_still_listed()
    {
        Assert.That(
            HasLiveDescendant(
                Entry(11, RootId, 110, alive: false),
                Entry(12, 11, 120)),
            Is.True);
    }

    [Test]
    public void Ignores_exited_descendants_and_unrelated_processes()
    {
        Assert.That(
            HasLiveDescendant(
                Entry(11, RootId, 110, alive: false),
                Entry(12, 11, 120, alive: false),
                Entry(99, 50, 90),
                Entry(RootId, 1, RootCreationTime)),
            Is.False);
    }

    // The root's id was held by an older process whose child is still running.
    [Test]
    public void Ignores_a_child_of_an_older_process_that_held_the_root_id()
    {
        Assert.That(HasLiveDescendant(Entry(11, RootId, RootCreationTime - 1)), Is.False);
    }

    // An exited child's id was taken by a newer process after the child had started a grandchild.
    [Test]
    public void Ignores_a_child_of_an_older_process_that_held_a_descendant_id()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HasLiveDescendant(
                    Entry(11, RootId, 150, alive: false),
                    Entry(12, 11, 120)),
                Is.False);
            Assert.That(
                HasLiveDescendant(
                    Entry(11, RootId, 110, alive: false),
                    Entry(12, 11, 110)),
                Is.True,
                "A child created in the same tick as its parent is still its child.");
        });
    }

    [Test]
    public void Terminates_on_a_listing_that_repeats_an_entry_or_loops_back_to_the_root()
    {
        Assert.That(
            HasLiveDescendant(
                Entry(11, RootId, 110, alive: false),
                Entry(11, RootId, 110, alive: false),
                Entry(12, 11, 120, alive: false),
                Entry(11, 12, 130, alive: false),
                Entry(RootId, 12, 140, alive: false)),
            Is.False);
    }

    [Test]
    public void Listing_is_read_entry_by_entry_until_the_last_one()
    {
        int size = Unsafe.SizeOf<WindowsInterop.SystemProcessInformation>();
        // Entries carry variable data after the fixed part, so the next one starts past it.
        byte[] listing = new byte[(size * 2) + 16];
        Write(listing, 0, nextEntryOffset: size + 16, threads: 3, createTime: 110, id: 11, parentId: RootId);
        Write(listing, size + 16, nextEntryOffset: 0, threads: 0, createTime: 120, id: 12, parentId: 11);
        var entries = new List<ProcessTreeEntry>();

        Assert.Multiple(() =>
        {
            Assert.That(SystemProcessListing.TryRead(listing, entries), Is.True);
            Assert.That(entries, Is.EqualTo(new[] { Entry(11, RootId, 110), Entry(12, 11, 120, alive: false) }));
        });
    }

    [Test]
    public void Listing_that_runs_past_its_end_is_not_read()
    {
        int size = Unsafe.SizeOf<WindowsInterop.SystemProcessInformation>();
        byte[] pointsPastTheEnd = new byte[size];
        Write(pointsPastTheEnd, 0, nextEntryOffset: size * 2, threads: 1, createTime: 110, id: 11, parentId: RootId);
        byte[] endsInsideAnEntry = new byte[size + 8];
        Write(endsInsideAnEntry, 0, nextEntryOffset: size, threads: 1, createTime: 110, id: 11, parentId: RootId);

        Assert.Multiple(() =>
        {
            Assert.That(SystemProcessListing.TryRead(pointsPastTheEnd, []), Is.False);
            Assert.That(SystemProcessListing.TryRead(endsInsideAnEntry, []), Is.False);
            Assert.That(SystemProcessListing.TryRead(new byte[size - 1], []), Is.False);
        });
    }

    [Test]
    public void Windows_interop_structures_match_their_native_layout()
    {
        if (IntPtr.Size != 8)
        {
            Assert.Ignore("The expected layout is that of a 64-bit process.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(Unsafe.SizeOf<WindowsInterop.SecurityAttributes>(), Is.EqualTo(24));
            Assert.That(Unsafe.SizeOf<WindowsInterop.StartupInfo>(), Is.EqualTo(104));
            Assert.That(Unsafe.SizeOf<WindowsInterop.ProcessInformation>(), Is.EqualTo(24));
            Assert.That(Unsafe.SizeOf<WindowsInterop.JobObjectBasicLimitInformation>(), Is.EqualTo(64));
            Assert.That(Unsafe.SizeOf<WindowsInterop.IoCounters>(), Is.EqualTo(48));
            Assert.That(Unsafe.SizeOf<WindowsInterop.JobObjectExtendedLimitInformation>(), Is.EqualTo(144));
            Assert.That(Unsafe.SizeOf<WindowsInterop.JobObjectBasicAccountingInformation>(), Is.EqualTo(48));
            Assert.That(OffsetOf(nameof(WindowsInterop.SystemProcessInformation.NumberOfThreads)), Is.EqualTo(4));
            Assert.That(OffsetOf(nameof(WindowsInterop.SystemProcessInformation.CreateTime)), Is.EqualTo(32));
            Assert.That(OffsetOf(nameof(WindowsInterop.SystemProcessInformation.ImageName)), Is.EqualTo(56));
            Assert.That(OffsetOf(nameof(WindowsInterop.SystemProcessInformation.UniqueProcessId)), Is.EqualTo(80));
            Assert.That(
                OffsetOf(nameof(WindowsInterop.SystemProcessInformation.InheritedFromUniqueProcessId)),
                Is.EqualTo(88));
        });
    }

    private static void Write(
        byte[] listing,
        int offset,
        int nextEntryOffset,
        int threads,
        long createTime,
        int id,
        int parentId)
    {
        var information = new WindowsInterop.SystemProcessInformation
        {
            NextEntryOffset = (uint)nextEntryOffset,
            NumberOfThreads = (uint)threads,
            CreateTime = createTime,
            UniqueProcessId = id,
            InheritedFromUniqueProcessId = parentId,
        };
        MemoryMarshal.Write(listing.AsSpan(offset), in information);
    }

    private static int OffsetOf(string field)
        => (int)Marshal.OffsetOf<WindowsInterop.SystemProcessInformation>(field);

    private static ProcessTreeEntry Entry(int id, int parentId, long creationTime, bool alive = true)
        => new(id, parentId, creationTime, alive);

    private static bool HasLiveDescendant(params ProcessTreeEntry[] listing)
        => ProcessTreeWalk.HasLiveDescendant(RootId, RootCreationTime, listing);
}
