using System.Collections.Specialized;

using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Language;
using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class PromptLibraryProviderTests
{
    private string _home = null!;
    private string _storePath = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Path.Combine(Path.GetTempPath(), $"beutl-prompt-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
        _storePath = Path.Combine(_home, "recovery.json");
        PromptLibraryProvider.ConfigureRootForTests(Path.Combine(_home, "ai-prompts"));
    }

    [TearDown]
    public void TearDown()
    {
        PromptLibraryProvider.ResetRootAfterTests();
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    [Test]
    public void UnauthenticatedReadsEmptyAndWritesReject()
    {
        string? account = null;
        using var context = Context(() => account);
        IPromptLibrary library = PromptLibraryProvider.For(context);

        Assert.That(library.History, Is.Empty);
        Assert.That(() => library.Record(PromptTaskKind.Image, "private"),
            Throws.TypeOf<AuthenticationRequiredException>());
    }

    [Test]
    public void AccountsAreIsolatedAndSwitchBackRetainsOwnData()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary library = PromptLibraryProvider.For(context);
        library.Record(PromptTaskKind.Image, "A prompt");
        library.SaveTemplate("A template", PromptTaskKind.Image, "A body");

        account = "account-b";
        IPromptLibrary other = PromptLibraryProvider.For(context);
        Assert.That(other.History, Is.Empty);
        Assert.That(other.Templates, Is.Empty);
        other.Record(PromptTaskKind.Image, "B prompt");
        Assert.That(other.DeleteHistory(other.History[0].Id), Is.True);

        account = "account-a";
        Assert.That(library.History.Select(item => item.Prompt), Is.EqualTo(["A prompt"]));
        Assert.That(library.Templates.Select(item => item.Name), Is.EqualTo(["A template"]));
    }

    [Test]
    public void AccountSwitchWithCorruptDestinationDoesNotAbortIdentityHandlers()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        var library = new ThrowingPromptLibrary(() => account == "account-b");
        using var viewModel = CreateViewModel(library, context);
        viewModel.TemplateName.Value = "old account";
        viewModel.IsHistoryOpen.Value = true;
        bool followingHandlerRan = false;
        context.IdentityChanged += () => followingHandlerRan = true;

        account = "account-b";
        Assert.DoesNotThrow(context.RefreshIdentity);
        Assert.Multiple(() =>
        {
            Assert.That(followingHandlerRan, Is.True);
            Assert.That(viewModel.History, Is.Empty);
            Assert.That(viewModel.Templates, Is.Empty);
            Assert.That(viewModel.TemplateName.Value, Is.Empty);
            Assert.That(viewModel.IsHistoryOpen.Value, Is.False);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Strings.AiResultUnavailable));
        });
    }

    [Test]
    public void AccountSwitchWithNewerFormatDoesNotAbortIdentityHandlers()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        var library = new ThrowingPromptLibrary(
            () => account == "account-b",
            new NotSupportedException("newer prompt library"));
        using var viewModel = CreateViewModel(library, context);
        bool followingHandlerRan = false;
        context.IdentityChanged += () => followingHandlerRan = true;

        account = "account-b";
        Assert.DoesNotThrow(context.RefreshIdentity);
        Assert.Multiple(() =>
        {
            Assert.That(followingHandlerRan, Is.True);
            Assert.That(viewModel.History, Is.Empty);
            Assert.That(viewModel.Templates, Is.Empty);
            Assert.That(viewModel.Error.Value, Is.EqualTo(Strings.AiResultUnavailable));
        });
    }

    [Test]
    public async Task IdentityChangeDuringInitialSnapshotCannotPublishThePreviousAccount()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        using var initialSnapshotStarted = new ManualResetEventSlim();
        using var releaseInitialSnapshot = new ManualResetEventSlim();
        using var identityPublished = new ManualResetEventSlim();
        context.IdentityChanged += identityPublished.Set;
        var library = new BlockingAccountPromptLibrary(
            () => account,
            initialSnapshotStarted,
            releaseInitialSnapshot);
        Task<AiPromptLibraryViewModel> creation = Task.Run(() => CreateViewModel(library, context));

        try
        {
            Assert.That(initialSnapshotStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
            account = "account-b";
            Task identityChange = Task.Run(context.RefreshIdentity);
            Assert.That(identityPublished.Wait(TimeSpan.FromSeconds(5)), Is.True);

            releaseInitialSnapshot.Set();
            using AiPromptLibraryViewModel viewModel =
                await creation.WaitAsync(TimeSpan.FromSeconds(5));
            await identityChange.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(
                viewModel.Templates.Select(item => item.Name),
                Is.EqualTo(["Account B"]));
        }
        finally
        {
            releaseInitialSnapshot.Set();
            if (creation.IsCompletedSuccessfully)
                creation.Result.Dispose();
        }
    }

    [Test]
    public void SameAccountViewModelsRefreshAfterEverySharedLibraryMutation()
    {
        using var context = Context(() => "account-a");
        IPromptLibrary writer = PromptLibraryProvider.For(context);
        using var first = CreateViewModel(PromptLibraryProvider.For(context), context);
        using var second = CreateViewModel(PromptLibraryProvider.For(context), context);

        PromptTemplate template = writer.SaveTemplate(
            "Shared",
            PromptTaskKind.Image,
            "shared template");
        PromptHistoryEntry history = writer.Record(
            PromptTaskKind.Image,
            "shared history");
        Assert.Multiple(() =>
        {
            Assert.That(first.Templates.Select(item => item.Id), Is.EqualTo([template.Id]));
            Assert.That(second.Templates.Select(item => item.Id), Is.EqualTo([template.Id]));
            Assert.That(first.History.Select(item => item.Id), Is.EqualTo([history.Id]));
            Assert.That(second.History.Select(item => item.Id), Is.EqualTo([history.Id]));
        });

        Assert.That(writer.SetTemplatePinned(template.Id, true), Is.True);
        Assert.That(writer.SetHistoryPinned(history.Id, true), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first.Templates.Single().IsPinned, Is.True);
            Assert.That(second.Templates.Single().IsPinned, Is.True);
            Assert.That(first.History.Single().IsPinned, Is.True);
            Assert.That(second.History.Single().IsPinned, Is.True);
        });

        Assert.That(writer.DeleteTemplate(template.Id), Is.True);
        writer.ClearHistory();
        Assert.Multiple(() =>
        {
            Assert.That(first.Templates, Is.Empty);
            Assert.That(second.Templates, Is.Empty);
            Assert.That(first.History, Is.Empty);
            Assert.That(second.History, Is.Empty);
        });
    }

    [Test]
    public async Task BackgroundMutationIsAppliedThroughTheCapturedUiDispatcher()
    {
        using var context = Context(() => "account-a");
        IPromptLibrary writer = PromptLibraryProvider.For(context);
        var dispatcher = new QueuedUiDispatcher();
        using var first = CreateViewModel(PromptLibraryProvider.For(context), context, dispatcher.Dispatch);
        using var second = CreateViewModel(PromptLibraryProvider.For(context), context, dispatcher.Dispatch);
        bool notificationRanDuringUiDrain = false;
        ((INotifyCollectionChanged)second.Templates).CollectionChanged += (_, _) =>
            notificationRanDuringUiDrain = dispatcher.IsDraining;

        dispatcher.Defer = true;
        await Task.Run(() =>
        {
            writer.SaveTemplate("Background", PromptTaskKind.Image, "background prompt");
        });
        Assert.That(second.Templates, Is.Empty);

        dispatcher.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(second.Templates.Select(item => item.Name), Is.EqualTo(["Background"]));
            Assert.That(notificationRanDuringUiDrain, Is.True);
        });
    }

    [Test]
    public void SharedLibrarySubscriptionFollowsAccountSwitchAndStopsAfterDispose()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary writer = PromptLibraryProvider.For(context);
        using var active = CreateViewModel(PromptLibraryProvider.For(context), context);
        var disposed = CreateViewModel(PromptLibraryProvider.For(context), context);

        writer.SaveTemplate("Account A", PromptTaskKind.Image, "private A");
        account = "account-b";
        context.RefreshIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(active.Templates, Is.Empty);
            Assert.That(disposed.Templates, Is.Empty);
        });

        writer.SaveTemplate("Account B", PromptTaskKind.Image, "private B");
        Assert.Multiple(() =>
        {
            Assert.That(active.Templates.Select(item => item.Name), Is.EqualTo(["Account B"]));
            Assert.That(disposed.Templates.Select(item => item.Name), Is.EqualTo(["Account B"]));
        });

        disposed.Dispose();
        writer.SaveTemplate("Account B 2", PromptTaskKind.Image, "private B 2");
        Assert.Multiple(() =>
        {
            Assert.That(
                active.Templates.Select(item => item.Name),
                Is.EqualTo(["Account B 2", "Account B"]));
            Assert.That(
                disposed.Templates.Select(item => item.Name),
                Is.EqualTo(["Account B"]));
        });

        account = "account-a";
        context.RefreshIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(active.Templates.Select(item => item.Name), Is.EqualTo(["Account A"]));
            Assert.That(disposed.Templates.Select(item => item.Name), Is.EqualTo(["Account B"]));
        });
    }

    [Test]
    public async Task QueuedLibraryRefreshRechecksAccountGenerationAndDisposal()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary writer = PromptLibraryProvider.For(context);
        var dispatcher = new QueuedUiDispatcher();
        using var active = CreateViewModel(PromptLibraryProvider.For(context), context, dispatcher.Dispatch);
        var disposed = CreateViewModel(PromptLibraryProvider.For(context), context, dispatcher.Dispatch);
        writer.SaveTemplate("Account A", PromptTaskKind.Image, "private A");

        dispatcher.Defer = true;
        await Task.Run(() =>
            writer.SaveTemplate("Stale A", PromptTaskKind.Image, "stale A"));
        account = "account-b";
        context.RefreshIdentity();
        disposed.Dispose();
        dispatcher.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(active.Templates, Is.Empty,
                "The queued account-A refresh must not publish after the account-B transition.");
            Assert.That(
                disposed.Templates.Select(item => item.Name),
                Is.EqualTo(["Account A"]),
                "A queued refresh must not mutate a disposed ViewModel.");
        });
    }

    [Test]
    public void SharedViewModelsRecoverAfterTransientAccountLibraryBindingFailure()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary writer = PromptLibraryProvider.For(context);
        using var first = CreateViewModel(PromptLibraryProvider.For(context), context);
        using var second = CreateViewModel(PromptLibraryProvider.For(context), context);
        writer.SaveTemplate("Account A", PromptTaskKind.Image, "private A");

        account = "account-b";
        // A library a newer Beutl wrote cannot be read until it is gone.
        string accountFile = Path.Combine(
            _home,
            "ai-prompts",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("account-b"))) + ".json");
        File.WriteAllText(
            accountFile,
            $$"""{ "version": {{PersistentPromptLibrary.CurrentStorageVersion + 1}}, "history": [], "templates": [] }""");
        context.RefreshIdentity();
        Assert.Multiple(() =>
        {
            Assert.That(first.Templates, Is.Empty);
            Assert.That(second.Templates, Is.Empty);
            Assert.That(first.Error.Value, Is.EqualTo(Strings.AiResultUnavailable));
            Assert.That(second.Error.Value, Is.EqualTo(Strings.AiResultUnavailable));
        });
        File.Delete(accountFile);

        // No second identity event occurs. The first successful mutation must publish
        // through the account-scoped hub and refresh every still-live workspace.
        PromptTemplate recovered = writer.SaveTemplate(
            "Recovered B",
            PromptTaskKind.Image,
            "private B");
        Assert.Multiple(() =>
        {
            Assert.That(first.Templates.Select(item => item.Id), Is.EqualTo([recovered.Id]));
            Assert.That(second.Templates.Select(item => item.Id), Is.EqualTo([recovered.Id]));
            Assert.That(first.Error.Value, Is.Null);
            Assert.That(second.Error.Value, Is.Null);
        });
    }

    [Test]
    public void FilesOfTheSingleFileLibraryAreIgnored()
    {
        // Only development builds of #2305 wrote ai-prompts.json; releases also wrote the migration marker.
        _ = new PersistentPromptLibrary(Path.Combine(_home, "ai-prompts.json"))
            .SaveTemplate("legacy", PromptTaskKind.Image, "legacy prompt");
        File.WriteAllText(Path.Combine(_home, "ai-prompts.migrated"), "invalid");
        using var context = Context(() => "account-a");
        IPromptLibrary library = PromptLibraryProvider.For(context);

        Assert.That(library.Templates, Is.Empty);
        library.Record(PromptTaskKind.Image, "A prompt");
        Assert.That(library.History.Select(item => item.Prompt), Is.EqualTo(["A prompt"]));
    }

    // An image prompt workspace that applies its UI updates inline unless dispatch is given.
    private static AiPromptLibraryViewModel CreateViewModel(
        IPromptLibrary library, AiRequestRecoveryContext context, Action<Action>? dispatch = null)
        => new(
            PromptTaskKind.Image,
            static () => string.Empty,
            static _ => { },
            library,
            context,
            dispatch ?? (static action => action()));

    private AiRequestRecoveryContext Context(Func<string?> account)
        => new(
            new FileAiRequestKeyMap(_storePath),
            () => account() is { } id
                ? new AiAuthenticatedRequestIdentity(id, User: null)
                : null);

    // An empty library that refuses writes; each fake overrides what it reads.
    private abstract class StubPromptLibrary : IPromptLibrary
    {
        public virtual IReadOnlyList<PromptHistoryEntry> History => [];
        public virtual IReadOnlyList<PromptTemplate> Templates => [];
        public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public bool SetHistoryPinned(Guid id, bool isPinned) => false;
        public bool SetTemplatePinned(Guid id, bool isPinned) => false;
        public bool DeleteHistory(Guid id) => false;
        public bool DeleteTemplate(Guid id) => false;
        public void ClearHistory() { }
    }

    private sealed class ThrowingPromptLibrary(
        Func<bool> shouldThrow,
        Exception? failure = null) : StubPromptLibrary
    {
        public override IReadOnlyList<PromptHistoryEntry> History
            => shouldThrow()
                ? throw failure ?? new InvalidDataException("corrupt")
                : Array.Empty<PromptHistoryEntry>();
    }

    private sealed class BlockingAccountPromptLibrary(
        Func<string?> account,
        ManualResetEventSlim initialSnapshotStarted,
        ManualResetEventSlim releaseInitialSnapshot)
        : StubPromptLibrary, IPromptLibraryChangeSource
    {
        private int _templateReads;

        public override IReadOnlyList<PromptTemplate> Templates
        {
            get
            {
                string name = account() == "account-a" ? "Account A" : "Account B";
                PromptTemplate[] snapshot =
                [
                    new PromptTemplate(
                        Guid.Parse("f1570c99-2514-4e24-ad69-350f745924b6"),
                        name,
                        PromptTaskKind.Image,
                        name,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch,
                        false),
                ];
                if (Interlocked.Increment(ref _templateReads) == 1)
                {
                    initialSnapshotStarted.Set();
                    if (!releaseInitialSnapshot.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The initial prompt snapshot was not released.");
                }
                return snapshot;
            }
        }

        public IDisposable SubscribeChanged(Action callback) => new MemoryStream();
    }

    private sealed class QueuedUiDispatcher
    {
        private readonly object _gate = new();
        private readonly Queue<Action> _queued = new();

        public bool Defer { get; set; }

        public bool IsDraining { get; private set; }

        public void Dispatch(Action action)
        {
            lock (_gate)
            {
                if (Defer)
                {
                    _queued.Enqueue(action);
                    return;
                }
            }
            action();
        }

        public void Drain()
        {
            IsDraining = true;
            try
            {
                while (true)
                {
                    Action? action;
                    lock (_gate)
                    {
                        action = _queued.Count > 0 ? _queued.Dequeue() : null;
                    }
                    if (action is null)
                        return;
                    action();
                }
            }
            finally
            {
                IsDraining = false;
            }
        }
    }
}
