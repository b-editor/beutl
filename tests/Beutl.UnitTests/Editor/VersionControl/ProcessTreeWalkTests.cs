using System.Runtime.CompilerServices;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

// The walk that finds a Windows command's descendants outside its job. It is platform neutral, so
// these run everywhere against a fake process table.
[TestFixture]
public class ProcessTreeWalkTests
{
    private const int RootId = 10;
    private const long RootCreationTime = 100;

    [Test]
    public void Walk_visits_live_descendants_and_searches_below_an_exited_one()
    {
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110),
            new FakeProcess(12, ParentId: 11, CreationTime: 120, Exited: true),
            new FakeProcess(13, ParentId: 12, CreationTime: 130),
            new FakeProcess(99, ParentId: 50, CreationTime: 90));

        IReadOnlyList<int> visited = table.Walk();

        Assert.Multiple(() =>
        {
            Assert.That(visited, Is.EquivalentTo(new[] { 11, 13 }));
            Assert.That(table.Opened, Does.Not.Contain(99));
            Assert.That(table.AllHandlesDisposed, Is.True);
        });
    }

    [Test]
    public void Walk_rejects_an_id_reused_since_the_listing()
    {
        // The listing names 11 as a child of the root, but the process now holding id 11 is not.
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110) { ActualParentId = 50 },
            new FakeProcess(12, ParentId: 11, CreationTime: 120));

        IReadOnlyList<int> visited = table.Walk();

        Assert.Multiple(() =>
        {
            Assert.That(visited, Is.Empty);
            Assert.That(table.Opened, Does.Not.Contain(12), "A rejected process is not searched.");
            Assert.That(table.AllHandlesDisposed, Is.True);
        });
    }

    [Test]
    public void Walk_rejects_a_child_of_an_older_process_that_held_the_parent_id()
    {
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: RootCreationTime - 1),
            new FakeProcess(12, ParentId: RootId, CreationTime: RootCreationTime));

        Assert.That(table.Walk(), Is.EquivalentTo(new[] { 12 }));
    }

    [Test]
    public void Walk_skips_a_process_it_cannot_open_or_identify()
    {
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110) { CanOpen = false },
            new FakeProcess(12, ParentId: RootId, CreationTime: 120) { CanIdentify = false },
            new FakeProcess(13, ParentId: RootId, CreationTime: 130));

        IReadOnlyList<int> visited = table.Walk();

        Assert.Multiple(() =>
        {
            Assert.That(visited, Is.EquivalentTo(new[] { 13 }));
            Assert.That(table.AllHandlesDisposed, Is.True);
        });
    }

    [Test]
    public void Walk_keeps_a_matched_parent_open_while_its_children_are_matched()
    {
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110),
            new FakeProcess(12, ParentId: 11, CreationTime: 120));
        bool parentOpenWhileChildVisited = false;

        table.Walk(handle =>
        {
            if (handle.Process.Id == 12)
            {
                parentOpenWhileChildVisited = table.IsOpen(11);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(parentOpenWhileChildVisited, Is.True);
            Assert.That(table.AllHandlesDisposed, Is.True);
        });
    }

    [Test]
    public void Walk_visits_each_process_once_and_never_the_root()
    {
        // A stale listing can repeat an entry or name the root as someone's child.
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110),
            new FakeProcess(11, ParentId: RootId, CreationTime: 110),
            new FakeProcess(RootId, ParentId: 11, CreationTime: RootCreationTime));

        Assert.That(table.Walk(), Is.EqualTo(new[] { 11 }));
    }

    // A process created after the cutoff left the job on purpose, and everything below it is newer.
    [Test]
    public void Walk_leaves_out_a_process_created_after_the_cutoff_and_its_children()
    {
        var table = new FakeProcessTable(
            new FakeProcess(11, ParentId: RootId, CreationTime: 110),
            new FakeProcess(12, ParentId: RootId, CreationTime: 150),
            new FakeProcess(13, ParentId: 12, CreationTime: 160),
            new FakeProcess(14, ParentId: 11, CreationTime: 140));

        IReadOnlyList<int> visited = table.Walk(createdNoLaterThan: 140);

        Assert.Multiple(() =>
        {
            Assert.That(visited, Is.EquivalentTo(new[] { 11, 14 }));
            Assert.That(table.Opened, Does.Not.Contain(13));
            Assert.That(table.AllHandlesDisposed, Is.True);
        });
    }

    [Test]
    public void Windows_job_interop_structures_match_their_native_sizes()
    {
        if (IntPtr.Size != 8)
        {
            Assert.Ignore("The expected sizes are those of a 64-bit process.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.JobObjectBasicLimitInformation>(), Is.EqualTo(64));
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.IoCounters>(), Is.EqualTo(48));
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.JobObjectExtendedLimitInformation>(), Is.EqualTo(144));
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.JobObjectBasicAccountingInformation>(), Is.EqualTo(48));
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.ProcessEntry>(), Is.EqualTo(568));
            Assert.That(Unsafe.SizeOf<ManagedGitProcess.ProcessBasicInformation>(), Is.EqualTo(48));
        });
    }

    private sealed record FakeProcess(int Id, int ParentId, long CreationTime, bool Exited = false)
    {
        public int? ActualParentId { get; init; }

        public bool CanOpen { get; init; } = true;

        public bool CanIdentify { get; init; } = true;
    }

    private sealed class FakeHandle(FakeProcess process) : IDisposable
    {
        public FakeProcess Process { get; } = process;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProcessTable(params FakeProcess[] processes)
    {
        private readonly List<FakeHandle> _handles = [];

        public List<int> Opened { get; } = [];

        public bool AllHandlesDisposed => _handles.All(static handle => handle.Disposed);

        public bool IsOpen(int id) => _handles.Any(handle => handle.Process.Id == id && !handle.Disposed);

        public IReadOnlyList<int> Walk(Action<FakeHandle>? onVisit = null, long createdNoLaterThan = long.MaxValue)
        {
            var visited = new List<int>();
            ProcessTreeWalk.VisitLiveDescendants(
                RootId,
                RootCreationTime,
                createdNoLaterThan,
                processes.Select(static process => new ProcessTreeEntry(process.Id, process.ParentId)).ToArray(),
                Open,
                static handle => handle.Process.CanIdentify
                    ? new ProcessTreeIdentity(
                        handle.Process.ActualParentId ?? handle.Process.ParentId,
                        handle.Process.CreationTime)
                    : null,
                static handle => handle.Process.Exited,
                handle =>
                {
                    visited.Add(handle.Process.Id);
                    onVisit?.Invoke(handle);
                });
            return visited;
        }

        private FakeHandle? Open(int id)
        {
            Opened.Add(id);
            FakeProcess? process = processes.FirstOrDefault(candidate => candidate.Id == id);
            if (process is not { CanOpen: true })
            {
                return null;
            }

            var handle = new FakeHandle(process);
            _handles.Add(handle);
            return handle;
        }
    }
}
