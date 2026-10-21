using Beutl.Api.Services;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal static class PromptLibraryProvider
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<string, PersistentPromptLibrary> s_libraries = new(StringComparer.Ordinal);
    private static string s_root = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ai-prompts");

    internal static void ConfigureRootForTests(string root)
    {
        lock (s_gate)
        {
            s_libraries.Clear();
            s_root = Path.GetFullPath(root);
        }
    }

    internal static void ResetRootAfterTests()
        => ConfigureRootForTests(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(),
            "ai-prompts"));

    public static IPromptLibrary For(AiRequestRecoveryContext context)
        => new AccountPromptLibrary(context);

    private static (string AccountKey, string StoragePath)? ResolveAccountStorage(
        AiRequestRecoveryContext context)
    {
        string account = context.TryGetIdentity()?.AccountId ?? string.Empty;
        if (account.Length == 0)
            return null;

        string accountKey = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(account)));
        return (accountKey, Path.Combine(s_root, accountKey + ".json"));
    }

    private sealed class AccountPromptLibrary(AiRequestRecoveryContext context)
        : IPromptLibrary, IPromptLibraryChangeSource
    {
        private PersistentPromptLibrary? Library
        {
            get
            {
                if (ResolveAccountStorage(context) is not { } account)
                    return null;
                Directory.CreateDirectory(s_root);
                string accountKey = account.AccountKey;
                string path = account.StoragePath;
                lock (s_gate)
                {
                    if (s_libraries.TryGetValue(accountKey, out PersistentPromptLibrary? cached))
                        return cached;

                    var library = new PersistentPromptLibrary(path);
                    s_libraries[accountKey] = library;
                    return library;
                }
            }
        }

        public IReadOnlyList<PromptHistoryEntry> History => Library?.History ?? Array.Empty<PromptHistoryEntry>();
        public IReadOnlyList<PromptTemplate> Templates => Library?.Templates ?? Array.Empty<PromptTemplate>();
        public PromptHistoryEntry Record(PromptTaskKind k, string p) => Require().Record(k, p);
        public PromptTemplate SaveTemplate(string n, PromptTaskKind k, string p) => Require().SaveTemplate(n, k, p);
        public bool SetHistoryPinned(Guid id, bool value) => Library?.SetHistoryPinned(id, value) ?? false;
        public bool SetTemplatePinned(Guid id, bool value) => Library?.SetTemplatePinned(id, value) ?? false;
        public bool DeleteHistory(Guid id) => Library?.DeleteHistory(id) ?? false;
        public bool DeleteTemplate(Guid id) => Library?.DeleteTemplate(id) ?? false;
        public void ClearHistory() => Library?.ClearHistory();
        public IDisposable SubscribeChanged(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return PromptLibraryChangeHub.Subscribe(path =>
            {
                if (ResolveAccountStorage(context) is { } current
                    && StringComparer.Ordinal.Equals(current.StoragePath, path))
                {
                    callback();
                }
            });
        }

        private PersistentPromptLibrary Require() => Library ?? throw new AuthenticationRequiredException();
    }
}
