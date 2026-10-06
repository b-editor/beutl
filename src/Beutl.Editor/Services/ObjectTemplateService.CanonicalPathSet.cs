namespace Beutl.Editor.Services;

public sealed partial class ObjectTemplateService
{
    private sealed class CanonicalPathSet
    {
        private readonly HashSet<string> _canonicalPaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _exactPaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _preferredPaths = new(StringComparer.Ordinal);
        private readonly FilePathComparison.ResolutionContext _resolutionContext;

        public CanonicalPathSet()
            : this(FilePathComparison.CreateResolutionContext())
        {
        }

        private CanonicalPathSet(FilePathComparison.ResolutionContext resolutionContext)
        {
            _resolutionContext = resolutionContext;
        }

        public static CanonicalPathSet FromEnumeratedFiles(IEnumerable<string> paths)
        {
            var result = new CanonicalPathSet(FilePathComparison.CreateResolutionContext());
            foreach (string path in paths)
            {
                string? canonicalPath = IsLiveFile(path)
                    && result.TryResolve(path, out string resolvedPath)
                    ? resolvedPath
                    : null;
                result.AddKnown(path, canonicalPath);
            }

            return result;
        }

        private static bool IsLiveFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.LinkTarget is null
                    ? info.Exists
                    : info.ResolveLinkTarget(returnFinalTarget: true)?.Exists == true;
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
            {
                return false;
            }
        }

        public void AddKnown(string path, string? canonicalPath)
        {
            _exactPaths[path] = canonicalPath;
            if (canonicalPath is not null)
            {
                _canonicalPaths.Add(canonicalPath);
                if (!_preferredPaths.TryGetValue(canonicalPath, out string? current)
                    || IsPreferredPath(path, current, canonicalPath))
                {
                    _preferredPaths[canonicalPath] = path;
                }
            }
        }

        public IEnumerable<string> GetPreferredPaths(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!_exactPaths.TryGetValue(path, out string? canonicalPath)
                    || canonicalPath is null)
                {
                    yield return path;
                    continue;
                }

                if (_preferredPaths.TryGetValue(canonicalPath, out string? preferredPath)
                    && string.Equals(path, preferredPath, StringComparison.Ordinal))
                {
                    yield return path;
                }
            }
        }

        public bool TryGetPreferredPath(string canonicalPath, out string? preferredPath)
        {
            return _preferredPaths.TryGetValue(canonicalPath, out preferredPath);
        }

        public bool TryGetExact(string path, out string? canonicalPath)
        {
            return _exactPaths.TryGetValue(path, out canonicalPath);
        }

        public bool TryMatch(string path, out string? canonicalPath)
        {
            if (_exactPaths.TryGetValue(path, out canonicalPath))
            {
                return canonicalPath is not null;
            }

            if (TryResolve(path, out string resolvedPath)
                && _canonicalPaths.Contains(resolvedPath))
            {
                canonicalPath = resolvedPath;
                return true;
            }

            canonicalPath = null;
            return false;
        }

        public bool ContainsKnown(string path, string? canonicalPath)
        {
            return _exactPaths.ContainsKey(path)
                   || canonicalPath is not null && _canonicalPaths.Contains(canonicalPath);
        }

        private bool TryResolve(string path, out string canonicalPath)
        {
            canonicalPath = string.Empty;
            try
            {
                canonicalPath = _resolutionContext.ResolveCanonicalPath(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
            {
                return false;
            }
        }

        private bool IsPreferredPath(
            string candidate,
            string current,
            string canonicalPath)
        {
            bool IsCanonicalEntry(string path)
            {
                string fullPath = Path.GetFullPath(path);
                // Resolve directory aliases without following the final file link: otherwise
                // every alias would qualify as the canonical entry.
                return TryResolve(Path.GetDirectoryName(fullPath)!, out string parent)
                    && string.Equals(
                        Path.Combine(parent, Path.GetFileName(fullPath)),
                        canonicalPath,
                        StringComparison.Ordinal);
            }

            bool candidateIsCanonical = IsCanonicalEntry(candidate);
            bool currentIsCanonical = IsCanonicalEntry(current);
            return candidateIsCanonical != currentIsCanonical
                ? candidateIsCanonical
                : string.CompareOrdinal(candidate, current) < 0;
        }
    }
}
