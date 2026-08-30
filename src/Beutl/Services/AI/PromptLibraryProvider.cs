using Beutl.Api.Services;

namespace Beutl.Services.AI;

internal static class PromptLibraryProvider
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<string, PersistentPromptLibrary> s_libraries = new(StringComparer.Ordinal);
    private static string s_root = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ai-prompts");
    private static string s_legacy = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ai-prompts.json");
    private static string s_migrationMarker = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ai-prompts.migrated");

    internal static void ConfigureRootForTests(string root)
    {
        lock (s_gate)
        {
            s_libraries.Clear();
            s_root = Path.GetFullPath(root);
            string home = Path.GetDirectoryName(s_root)!;
            s_legacy = Path.Combine(home, "ai-prompts.json");
            s_migrationMarker = Path.Combine(home, "ai-prompts.migrated");
        }
    }

    internal static void ResetRootAfterTests()
        => ConfigureRootForTests(Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(),
            "ai-prompts"));

    public static IPromptLibrary For(AiRequestRecoveryContext context)
        => new AccountPromptLibrary(context);

    private sealed class AccountPromptLibrary(AiRequestRecoveryContext context) : IPromptLibrary
    {
        private PersistentPromptLibrary? Library
        {
            get
            {
                string account = context.TryGetIdentity()?.AccountId
                    ?? string.Empty;
                if (account.Length == 0)
                    return null;
                Directory.CreateDirectory(s_root);
                string accountKey = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(account)));
                string path = Path.Combine(s_root, accountKey + ".json");
                lock (s_gate)
                {
                    if (s_libraries.TryGetValue(accountKey, out PersistentPromptLibrary? cached))
                        return cached;
                    using FileStream migrationLock = AcquireMigrationLock();
                    string? migrationOwner = ReadMigrationOwner();
                    if (migrationOwner is null)
                    {
                    }
                    else if (migrationOwner.Length == 0)
                    {
                        throw new InvalidDataException("Prompt migration marker is invalid.");
                    }
                    else if (migrationOwner is { Length: > 0 }
                        && !StringComparer.OrdinalIgnoreCase.Equals(migrationOwner, accountKey))
                    {
                        if (File.Exists(path))
                        {
                            var existing = new PersistentPromptLibrary(path);
                            s_libraries[accountKey] = existing;
                            return existing;
                        }
                        var isolated = new PersistentPromptLibrary(path);
                        s_libraries[accountKey] = isolated;
                        return isolated;
                    }

                    if (File.Exists(s_legacy) && !File.Exists(path))
                    {
                        try
                        {
                            if (migrationOwner is null)
                                WriteMigrationMarker(accountKey);
                            DurableMove(s_legacy, path, overwrite: false);
                            migrationOwner = accountKey;
                        }
                        catch (IOException ex)
                        {
                            throw new IOException("Prompt library legacy migration is pending; retry for the owning account.", ex);
                        }
                    }
                    else if (migrationOwner is not null && migrationOwner.Length > 0
                        && StringComparer.OrdinalIgnoreCase.Equals(migrationOwner, accountKey)
                        && !File.Exists(s_legacy) && !File.Exists(path))
                    {
                        throw new IOException("Prompt library migration owner data is missing.");
                    }
                    else if (migrationOwner is null
                        && !File.Exists(s_legacy)
                        && File.Exists(path))
                    {
                        try
                        {
                            WriteMigrationMarker(accountKey);
                        }
                        catch (IOException ex)
                        {
                            throw new IOException("Prompt library migration marker publication is pending.", ex);
                        }
                    }
                    else if (migrationOwner is null && File.Exists(s_legacy))
                    {
                        throw new IOException("Prompt library legacy migration is unavailable.");
                    }
                    var library = new PersistentPromptLibrary(path);
                    s_libraries[accountKey] = library;
                    return library;
                }
            }
        }

        private static void WriteMigrationMarker(string accountKey)
        {
            string temporary = s_migrationMarker + $".{Guid.NewGuid():N}.tmp";
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new(temporary, options))
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(accountKey);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                DurableMove(temporary, s_migrationMarker, overwrite: false);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static FileStream AcquireMigrationLock()
        {
            string path = s_migrationMarker + ".lock";
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                throw new IOException("Prompt library migration is busy; retry later.", ex);
            }
        }

        private static void DurableMove(string source, string destination, bool overwrite)
        {
            if (OperatingSystem.IsWindows())
            {
                const uint replace = 0x1;
                const uint writeThrough = 0x8;
                if (!MoveFileEx(source, destination, writeThrough | (overwrite ? replace : 0)))
                    throw new IOException("Prompt migration rename failed.", new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
            }
            else
            {
                File.Move(source, destination, overwrite);
                SyncDirectory(Path.GetDirectoryName(source)!);
                string destinationDirectory = Path.GetDirectoryName(destination)!;
                if (!StringComparer.Ordinal.Equals(
                        Path.GetFullPath(destinationDirectory),
                        Path.GetFullPath(Path.GetDirectoryName(source)!)))
                    SyncDirectory(destinationDirectory);
            }
        }

        private static void SyncDirectory(string path)
        {
            int fd = UnixOpen(path, 0);
            if (fd < 0)
                throw new IOException("Unable to open migration directory.");
            try
            {
                if (UnixFsync(fd) != 0)
                    throw new IOException("Unable to fsync migration directory.");
            }
            finally
            {
                _ = UnixClose(fd);
            }
        }

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int UnixOpen(string path, int flags);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        private static extern int UnixFsync(int fd);

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int UnixClose(int fd);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

        private static string? ReadMigrationOwner()
        {
            if (!File.Exists(s_migrationMarker))
                return null;
            try
            {
                string value = File.ReadAllText(s_migrationMarker).Trim();
                if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("Prompt migration marker is invalid.");
                return value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Prompt migration marker cannot be read.", ex);
            }
        }
        public string StoragePath => Library?.StoragePath ?? string.Empty;
        public bool RetainRecentPromptText => Library?.RetainRecentPromptText ?? false;
        public string? RecoveredCorruptFilePath => Library?.RecoveredCorruptFilePath;
        public IReadOnlyList<PromptHistoryEntry> History => Library?.History ?? Array.Empty<PromptHistoryEntry>();
        public IReadOnlyList<PromptTemplate> Templates => Library?.Templates ?? Array.Empty<PromptTemplate>();
        public PromptHistoryEntry Record(PromptTaskKind k, string p) => Require().Record(k, p);
        public PromptTemplate SaveTemplate(string n, PromptTaskKind k, string p) => Require().SaveTemplate(n, k, p);
        public bool SetHistoryPinned(Guid id, bool value) => Library?.SetHistoryPinned(id, value) ?? false;
        public bool SetTemplatePinned(Guid id, bool value) => Library?.SetTemplatePinned(id, value) ?? false;
        public bool DeleteHistory(Guid id) => Library?.DeleteHistory(id) ?? false;
        public bool DeleteTemplate(Guid id) => Library?.DeleteTemplate(id) ?? false;
        public void ClearHistory() => Library?.ClearHistory();
        public void ClearTemplates() => Library?.ClearTemplates();
        public void ClearAll() => Library?.ClearAll();
        private PersistentPromptLibrary Require() => Library ?? throw new AuthenticationRequiredException();
    }
}
