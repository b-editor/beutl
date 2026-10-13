namespace Beutl;

public static partial class FilePathComparison
{
    internal sealed class ResolutionContext
    {
        // HRESULT_FROM_WIN32(ERROR_NETWORK_ACCESS_DENIED). Windows reports a share that refuses
        // enumeration with this code, and .NET raises it as a plain IOException. Unix raises EACCES
        // and EPERM as UnauthorizedAccessException, and its other I/O errors carry a raw errno.
        private const int NetworkAccessDeniedHResult = unchecked((int)0x80070041);

        private readonly Func<string, string[]> _listDirectory;
        private readonly bool _useNativeEntryNames;

        // A null value records a directory whose listing a root-containment lookup was denied.
        private readonly Dictionary<string, DirectoryEntries?> _directoryEntries =
            new(StringComparer.Ordinal);

        public ResolutionContext()
            : this(Directory.GetFileSystemEntries, useNativeEntryNames: true)
        {
        }

        // Lets tests reproduce listing failures that a local filesystem cannot raise.
        internal ResolutionContext(Func<string, string[]> listDirectory, bool useNativeEntryNames = false)
        {
            _listDirectory = listDirectory;
            _useNativeEntryNames = useNativeEntryNames;
        }

        public string ResolveCanonicalPath(string path)
        {
            return FilePathComparison.ResolveCanonicalPath(path, this, requireListing: true);
        }

        /// <summary>
        /// Resolves <paramref name="path"/> and determines whether it is
        /// <paramref name="canonicalRoot"/> or one of its descendants.
        /// </summary>
        /// <param name="canonicalRoot">A root returned by <see cref="ResolveCanonicalPath(string)"/>.</param>
        /// <param name="path">The path to resolve.</param>
        /// <param name="identity">
        /// The resolved path. It is comparable only with roots resolved by this context.
        /// </param>
        /// <remarks>
        /// Unlike <see cref="ResolveCanonicalPath(string)"/>, an entry beneath an ancestor that cannot be listed keeps its
        /// supplied spelling, and symbolic links are still followed. The answer stays exact because resolving the root
        /// verified the spelling of each of the root's existing ancestors.
        /// </remarks>
        public bool IsSameOrDescendantOfCanonicalRoot(
            string canonicalRoot,
            string path,
            out string identity)
        {
            identity = FilePathComparison.ResolveCanonicalPath(path, this, requireListing: false);
            return FilePathComparison.IsSameOrDescendantCanonicalPath(canonicalRoot, identity);
        }

        internal string SelectEntry(
            string directory,
            string component,
            string candidate,
            bool requireListing)
        {
            // Query only this entry on macOS. Listing every sibling here makes even an empty
            // project's lifecycle scale with the size of unrelated ancestor/temp directories.
            if (_useNativeEntryNames && OperatingSystem.IsMacOS()
                && TryGetMacExistingEntry(directory, component, candidate) is { } nativeEntry)
            {
                return nativeEntry;
            }

            // A strict resolution retries a denial cached by a containment lookup, so it reports
            // the filesystem error instead of accepting an unverified spelling.
            if (!_directoryEntries.TryGetValue(directory, out DirectoryEntries? entries)
                || entries is null && requireListing)
            {
                entries = ListEntries(directory, requireListing);
                _directoryEntries[directory] = entries;
            }

            return entries?.Select(component, candidate) ?? candidate;
        }

        private DirectoryEntries? ListEntries(string directory, bool requireListing)
        {
            try
            {
                return new DirectoryEntries(_listDirectory(directory));
            }
            catch (Exception ex) when (!requireListing && IsListingDenied(ex))
            {
                return null;
            }
        }

        // Only a refusal to list counts. Other failures, such as a sharing violation, a network
        // error, or a directory that vanished, still abort the lookup.
        private static bool IsListingDenied(Exception exception)
            => exception is UnauthorizedAccessException
                or IOException { HResult: NetworkAccessDeniedHResult };

        internal sealed class DirectoryEntries(string[] paths)
        {
            private readonly Dictionary<string, string> _exact = IndexByName(paths);

            public string Select(string component, string candidate)
                => _exact.TryGetValue(component, out string? exact)
                    ? exact
                    : SelectCanonicalExistingEntry(component, candidate, paths);

            // A directory that changes while it is being listed can report one name twice;
            // both reports carry the same path, so the first one is kept.
            private static Dictionary<string, string> IndexByName(string[] entries)
            {
                var exact = new Dictionary<string, string>(entries.Length, StringComparer.Ordinal);
                foreach (string entry in entries)
                {
                    exact.TryAdd(Path.GetFileName(entry), entry);
                }

                return exact;
            }
        }
    }
}
