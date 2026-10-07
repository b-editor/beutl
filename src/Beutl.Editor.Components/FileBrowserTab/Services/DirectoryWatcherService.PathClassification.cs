using System.Text;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

internal sealed partial class DirectoryWatcherService
{
    private sealed record DirectoryIdentity(string LinkFingerprint, string CanonicalPath);

    private readonly record struct SpecialDirectoryMembershipKey(
        string TemplatesDirectory,
        string MaterialsDirectory,
        string CandidateDirectory);

    // プロジェクト、シーン、要素のファイルは頻繁に変更されるため除外
    internal bool ShouldExcludePath(string path)
    {
        return ShouldExcludePath(
            path,
            BeutlEnvironment.GetTemplatesDirectoryPath(),
            BeutlEnvironment.GetMaterialsDirectoryPath());
    }

    internal bool ShouldExcludePath(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        // Atomic saves create <document>.<guid:N>.tmp before replacing the document. These
        // transient events must not rebuild the browser on every edit, even inside templates.
        // Directories with these names are visible entries too.
        if (IsEditorSaveTemporaryFile(path) && !Directory.Exists(path))
        {
            return true;
        }

        // Templates and materials live below BEUTL_HOME/.beutl by default, so their explicit
        // exception must win over the reserved-metadata rule. Cache by containing directory: a
        // watcher burst commonly reports hundreds of sibling files, and canonical resolution only
        // needs to run once for that directory identity.
        if (IsTemplateOrMaterialPath(
                path,
                templatesDirectoryPath,
                materialsDirectoryPath))
        {
            return false;
        }

        if (HasReservedMetadataSegment(path))
        {
            return true;
        }

        return IsEditorDocument(path) && !Directory.Exists(path);
    }

    private bool ShouldCheckEntries(string path)
        => (IsEditorDocument(path) || IsEditorSaveTemporaryFile(path))
           && (!HasReservedMetadataSegment(path)
               || IsTemplateOrMaterialPath(path,
                   BeutlEnvironment.GetTemplatesDirectoryPath(),
                   BeutlEnvironment.GetMaterialsDirectoryPath()));

    internal static bool IsEditorSaveTemporaryFile(ReadOnlySpan<char> path)
    {
        if (!path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<char> name = path[..^4];
        int separator = name.LastIndexOf('.');
        return separator >= 0
               // TryParseExact trims whitespace; generated suffixes must already be 32 hex digits.
               && name.Length - separator - 1 == 32
               && Guid.TryParseExact(name[(separator + 1)..], "N", out _)
               && IsEditorDocument(name[..separator]);
    }

    private static bool IsEditorDocument(ReadOnlySpan<char> path)
        => path.EndsWith(".bep", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".belm", StringComparison.OrdinalIgnoreCase);

    private bool IsTemplateOrMaterialPath(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        if (IsConfiguredSpecialDirectory(
                path,
                templatesDirectoryPath,
                materialsDirectoryPath))
        {
            return true;
        }

        string? directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string canonicalDirectory;
        string canonicalTemplatesDirectory;
        string canonicalMaterialsDirectory;
        try
        {
            canonicalDirectory = ResolveDirectoryIdentity(Path.GetFullPath(directory));
            canonicalTemplatesDirectory = ResolveConfiguredDirectoryIdentity(
                templatesDirectoryPath);
            canonicalMaterialsDirectory = ResolveConfiguredDirectoryIdentity(
                materialsDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }

        if (_templateOrMaterialDirectories.Count >= 512)
        {
            _templateOrMaterialDirectories.Clear();
            _directoryIdentities.Clear();
        }

        return _templateOrMaterialDirectories.GetOrAdd(
            new SpecialDirectoryMembershipKey(
                canonicalTemplatesDirectory,
                canonicalMaterialsDirectory,
                canonicalDirectory),
            static key => FilePathComparison.IsSameOrDescendant(
                              key.TemplatesDirectory,
                              key.CandidateDirectory)
                          || FilePathComparison.IsSameOrDescendant(
                              key.MaterialsDirectory,
                              key.CandidateDirectory));
    }

    private bool IsConfiguredSpecialDirectory(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string fullTemplatesDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                templatesDirectoryPath));
            string fullMaterialsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                materialsDirectoryPath));
            if (string.Equals(fullPath, fullTemplatesDirectory, StringComparison.Ordinal)
                || string.Equals(fullPath, fullMaterialsDirectory, StringComparison.Ordinal))
            {
                return true;
            }

            // Files cannot be either special directory. Resolve their containing directory
            // through the shared membership cache instead of enumerating every sibling file.
            if (!Directory.Exists(fullPath))
                return false;

            string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
            return string.Equals(
                       canonicalPath,
                       ResolveConfiguredDirectoryIdentity(templatesDirectoryPath),
                       StringComparison.Ordinal)
                   || string.Equals(
                       canonicalPath,
                       ResolveConfiguredDirectoryIdentity(materialsDirectoryPath),
                       StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    private string ResolveConfiguredDirectoryIdentity(string directory)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(fullPath))
        {
            string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
            _configuredSpecialDirectoryIdentities[fullPath] = canonicalPath;
            return canonicalPath;
        }

        return _configuredSpecialDirectoryIdentities.TryGetValue(
            fullPath,
            out string? previousIdentity)
            ? previousIdentity
            : FilePathComparison.ResolveCanonicalPath(fullPath);
    }

    private string ResolveDirectoryIdentity(string fullPath)
    {
        string fingerprint = CreateLinkFingerprint(fullPath);
        if (_directoryIdentities.TryGetValue(fullPath, out DirectoryIdentity? identity)
            && string.Equals(identity.LinkFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return identity.CanonicalPath;
        }

        string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
        _directoryIdentities[fullPath] = new DirectoryIdentity(fingerprint, canonicalPath);
        return canonicalPath;
    }

    private static string CreateLinkFingerprint(string directory)
    {
        string root = Path.GetPathRoot(directory)
                      ?? throw new ArgumentException(
                          "The directory has no filesystem root.",
                          nameof(directory));
        string current = root;
        var fingerprint = new StringBuilder();
        foreach (string segment in directory[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            string? linkTarget = info.LinkTarget;
            fingerprint.Append(segment)
                .Append('=')
                .Append(linkTarget);
            if (linkTarget is not null)
            {
                fingerprint.Append("->")
                    .Append(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName);
            }

            fingerprint
                .Append('\0');
        }

        return fingerprint.ToString();
    }

    private static bool HasReservedMetadataSegment(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new ArgumentException("The path has no filesystem root.", nameof(path));
        string parent = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (AreSameChildPath(parent, segment, ".git")
                || AreSameChildPath(parent, segment, ".beutl"))
            {
                return true;
            }

            parent = Path.Combine(parent, segment);
        }

        return false;
    }

    private static bool AreSameChildPath(string parent, string leftName, string rightName)
    {
        return FilePathComparison.TryAreSameChildPath(
                   parent,
                   leftName,
                   rightName,
                   out bool areSame)
               && areSame;
    }
}
