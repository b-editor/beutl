using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;

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
    public void AccountSwitchClearsVisiblePromptsBeforeUnreadableDestinationIsOpened()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary library = PromptLibraryProvider.For(context);
        library.Record(PromptTaskKind.Image, "A private prompt");
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            static () => string.Empty,
            static _ => { },
            library,
            context);
        Assert.That(viewModel.History.Select(item => item.Prompt),
            Is.EqualTo(["A private prompt"]));

        File.WriteAllText(Path.Combine(_home, "ai-prompts.migrated"), "invalid");
        viewModel.TemplateName.Value = "A private template name";
        viewModel.IsHistoryOpen.Value = true;
        account = "account-b";

        Assert.DoesNotThrow(context.RefreshIdentity);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.History, Is.Empty);
            Assert.That(viewModel.Templates, Is.Empty);
            Assert.That(viewModel.HasHistory.Value, Is.False);
            Assert.That(viewModel.HasTemplates.Value, Is.False);
            Assert.That(viewModel.IsHistoryOpen.Value, Is.False);
            Assert.That(viewModel.TemplateName.Value, Is.Empty);
            Assert.That(viewModel.Error.Value, Is.Null,
                "An empty account store is a valid isolated destination, not an error.");
        });
    }

    [Test]
    public void AccountSwitchWithCorruptDestinationDoesNotAbortIdentityHandlers()
    {
        string? account = "account-a";
        using var context = Context(() => account);
        var library = new ThrowingPromptLibrary(() => account == "account-b");
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            static () => string.Empty,
            static _ => { },
            library,
            context);
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
            Assert.That(viewModel.Error.Value, Is.Null);
        });
    }

    [Test]
    public void LegacyFileIsClaimedOnceByFirstAuthenticatedAccount()
    {
        string legacy = Path.Combine(_home, "ai-prompts.json");
        var seed = new PersistentPromptLibrary(legacy, new PromptLibraryOptions { RetainRecentPromptText = true });
        seed.SaveTemplate("legacy", PromptTaskKind.Image, "legacy prompt");

        string? account = "account-a";
        using var context = Context(() => account);
        IPromptLibrary first = PromptLibraryProvider.For(context);
        Assert.That(first.Templates.Select(item => item.Prompt), Is.EqualTo(["legacy prompt"]));
        string marker = Path.Combine(_home, "ai-prompts.migrated");
        Assert.That(File.Exists(marker), Is.True);
        Assert.That(File.ReadAllText(marker), Is.EqualTo(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("account-a")))));

        account = "account-b";
        IPromptLibrary second = PromptLibraryProvider.For(context);
        Assert.That(second.History, Is.Empty);
        Assert.That(second.Templates, Is.Empty);
    }

    [Test]
    public void LegacyMigrationFailureIsFailClosedAndOwnerCanRetry()
    {
        string legacy = Path.Combine(_home, "ai-prompts.json");
        var seed = new PersistentPromptLibrary(legacy);
        seed.SaveTemplate("legacy", PromptTaskKind.Image, "legacy prompt");

        // Block the owner's destination. Marker ownership is still durable,
        // while the legacy move fails closed without exposing its data.
        string marker = Path.Combine(_home, "ai-prompts.migrated");
        string? account = "account-a";
        using var context = Context(() => account);
        string accountFile = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("account-a"))) + ".json";
        Directory.CreateDirectory(Path.Combine(_home, "ai-prompts"));
        Directory.CreateDirectory(Path.Combine(_home, "ai-prompts", accountFile));
        IPromptLibrary first = PromptLibraryProvider.For(context);
        Assert.That(() => first.Templates, Throws.TypeOf<IOException>());
        Assert.That(File.ReadAllText(marker), Is.EqualTo(accountFile[..^5]));
        Assert.That(File.Exists(Path.Combine(_home, "ai-prompts", accountFile)), Is.False);

        Directory.Delete(Path.Combine(_home, "ai-prompts", accountFile));
        account = "account-b";
        IPromptLibrary other = PromptLibraryProvider.For(context);
        other.Record(PromptTaskKind.Image, "B only");
        Assert.That(File.Exists(legacy), Is.True);
        account = "account-a";
        PromptLibraryProvider.ConfigureRootForTests(Path.Combine(_home, "ai-prompts"));
        IPromptLibrary retried = PromptLibraryProvider.For(context);
        Assert.That(retried.Templates.Select(item => item.Prompt), Is.EqualTo(["legacy prompt"]));
        Assert.That(File.Exists(marker), Is.True);
    }

    [Test]
    public void InvalidMigrationMarkerFailsClosedWhileLegacyRemains()
    {
        string legacy = Path.Combine(_home, "ai-prompts.json");
        _ = new PersistentPromptLibrary(legacy).SaveTemplate("legacy", PromptTaskKind.Image, "private");
        File.WriteAllText(Path.Combine(_home, "ai-prompts.migrated"), "invalid");
        using var context = Context(() => "account-a");
        Assert.That(() => PromptLibraryProvider.For(context).Templates, Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(legacy), Is.True);
    }

    [Test]
    public void PendingOwnerMarkerWithLegacyRetriesMove()
    {
        string legacy = Path.Combine(_home, "ai-prompts.json");
        _ = new PersistentPromptLibrary(legacy).SaveTemplate("legacy", PromptTaskKind.Image, "pending");
        string owner = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("account-a")));
        File.WriteAllText(Path.Combine(_home, "ai-prompts.migrated"), owner);
        using var context = Context(() => "account-a");
        IPromptLibrary library = PromptLibraryProvider.For(context);
        Assert.That(library.Templates.Select(item => item.Prompt), Is.EqualTo(["pending"]));
        Assert.That(File.Exists(legacy), Is.False);
    }

    [Test]
    public void PendingOwnerMarkerWithAccountPathCompletesWithoutLegacy()
    {
        string owner = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("account-a")));
        File.WriteAllText(Path.Combine(_home, "ai-prompts.migrated"), owner);
        string path = Path.Combine(_home, "ai-prompts", owner + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _ = new PersistentPromptLibrary(path).SaveTemplate("existing", PromptTaskKind.Image, "path");
        using var context = Context(() => "account-a");
        Assert.That(PromptLibraryProvider.For(context).Templates.Select(item => item.Prompt), Is.EqualTo(["path"]));
    }

    private AiRequestRecoveryContext Context(Func<string?> account)
        => new(
            new FileAiRequestRecoveryStore(_storePath),
            () => account() is { } id
                ? new AiAuthenticatedRequestIdentity(id, User: null)
                : null);

    private sealed class ThrowingPromptLibrary(Func<bool> shouldThrow) : IPromptLibrary
    {
        public string StoragePath => string.Empty;
        public bool RetainRecentPromptText => false;
        public string? RecoveredCorruptFilePath => null;
        public IReadOnlyList<PromptHistoryEntry> History
            => shouldThrow() ? throw new InvalidDataException("corrupt") : Array.Empty<PromptHistoryEntry>();
        public IReadOnlyList<PromptTemplate> Templates => Array.Empty<PromptTemplate>();
        public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt) => throw new NotSupportedException();
        public bool SetHistoryPinned(Guid id, bool isPinned) => false;
        public bool SetTemplatePinned(Guid id, bool isPinned) => false;
        public bool DeleteHistory(Guid id) => false;
        public bool DeleteTemplate(Guid id) => false;
        public void ClearHistory() { }
        public void ClearTemplates() { }
        public void ClearAll() { }
    }
}
