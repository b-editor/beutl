using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.Components.AiWorkspaceTab.Services;

namespace Beutl.HeadlessUITests;

// Caption drafts kept in memory. When FailOnSave is set, the save with that number (counting from 1) throws.
internal sealed class InMemoryCaptionDraftStore : ICaptionDraftStore
{
    private readonly Dictionary<CaptionDraftScope, CaptionDraftEntry> _drafts = [];
    private readonly HashSet<CaptionDraftScope> _owners = [];
    private readonly object _gate = new();
    private int _saveCount;

    public int? FailOnSave { get; set; }

    public bool TryOpen(
        CaptionDraftScope scope,
        [NotNullWhen(true)] out ICaptionDraftSession? session)
    {
        lock (_gate)
        {
            if (!_owners.Add(scope))
            {
                session = null;
                return false;
            }

            session = new Session(this, scope);
            return true;
        }
    }

    private sealed class Session(InMemoryCaptionDraftStore owner, CaptionDraftScope scope)
        : ICaptionDraftSession
    {
        private InMemoryCaptionDraftStore? _owner = owner;

        public CaptionDraftScope Scope { get; } = scope;

        public CaptionDraftReadResult Read()
        {
            InMemoryCaptionDraftStore store = GetOwner();
            lock (store._gate)
            {
                return store._drafts.GetValueOrDefault(Scope) is { } entry
                    ? new CaptionDraftReadResult(CaptionDraftReadOutcome.Read, entry)
                    : CaptionDraftReadResult.Absent;
            }
        }

        public void Save(CaptionDraftEntry entry)
        {
            InMemoryCaptionDraftStore store = GetOwner();
            lock (store._gate)
            {
                int save = ++store._saveCount;
                if (store.FailOnSave == save)
                    throw new IOException("Injected caption draft save failure.");
                store._drafts[Scope] = entry;
            }
        }

        public void Delete()
        {
            InMemoryCaptionDraftStore store = GetOwner();
            lock (store._gate)
            {
                store._drafts.Remove(Scope);
            }
        }

        public void Dispose()
        {
            InMemoryCaptionDraftStore? store = Interlocked.Exchange(ref _owner, null);
            if (store is null)
                return;

            lock (store._gate)
            {
                store._owners.Remove(Scope);
            }
        }

        private InMemoryCaptionDraftStore GetOwner()
            => _owner ?? throw new ObjectDisposedException(nameof(Session));
    }
}
