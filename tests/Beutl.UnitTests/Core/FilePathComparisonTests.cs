using System.Runtime.Versioning;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class FilePathComparisonTests
{
    [Test]
    public void DistinctNonLinkChildren_DoNotRequireParentEnumeration()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Unix directory permissions are required.");
        string parent = CreateTemporaryDirectory();
        File.WriteAllText(Path.Combine(parent, "clip"), "a");
        File.WriteAllText(Path.Combine(parent, ".beutl"), "b");
        UnixFileMode original = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.UserExecute);
            Assert.That(FilePathComparison.TryAreSameChildPath(parent, "clip", ".beutl", out bool same), Is.True);
            Assert.That(same, Is.False);
        }
        finally
        {
            File.SetUnixFileMode(parent, original);
            Directory.Delete(parent, true);
        }
    }

    [Test]
    public void Canonical_identity_accepts_whitespace_only_posix_components()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows does not preserve whitespace-only path components.");
        }

        string temporaryRoot = CreateTemporaryDirectory();
        string whitespacePath = Path.Combine(temporaryRoot, " ");
        Directory.CreateDirectory(whitespacePath);
        try
        {
            string expectedPath = Path.Combine(FilePathComparison.ResolveCanonicalPath(temporaryRoot), " ");
            Assert.Multiple(() =>
            {
                Assert.That(
                    FilePathComparison.ResolveCanonicalPath(whitespacePath),
                    Is.EqualTo(expectedPath));
                Assert.That(
                    FilePathComparison.TryAreSameChildPath(
                        temporaryRoot,
                        " ",
                        " ",
                        out bool areSame),
                    Is.True);
                Assert.That(areSame, Is.True);
            });
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Canonical_identity_follows_the_actual_volume_casing_rules()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string storedPath = Path.Combine(temporaryRoot, "Templates");
        string alternatePath = Path.Combine(temporaryRoot, "templates");
        Directory.CreateDirectory(storedPath);
        try
        {
            bool volumeResolvesAlternateCasing = Directory.Exists(alternatePath);

            Assert.That(
                FilePathComparison.AreSameCanonicalPath(storedPath, alternatePath),
                Is.EqualTo(volumeResolvesAlternateCasing));

            if (!volumeResolvesAlternateCasing)
            {
                Directory.CreateDirectory(alternatePath);
                Assert.That(
                    FilePathComparison.AreSameCanonicalPath(storedPath, alternatePath),
                    Is.False);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Canonical_identity_follows_the_actual_volume_unicode_normalization_rules()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string composedPath = Path.Combine(temporaryRoot, "caf\u00e9");
        string decomposedPath = Path.Combine(temporaryRoot, "cafe\u0301");
        Directory.CreateDirectory(composedPath);
        try
        {
            bool volumeResolvesAlternateNormalization = Directory.Exists(decomposedPath);

            Assert.That(
                FilePathComparison.AreSameCanonicalPath(composedPath, decomposedPath),
                Is.EqualTo(volumeResolvesAlternateNormalization));

            if (!volumeResolvesAlternateNormalization)
            {
                Directory.CreateDirectory(decomposedPath);
                Assert.That(
                    FilePathComparison.AreSameCanonicalPath(composedPath, decomposedPath),
                    Is.False);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Canonical_entry_selection_accepts_one_equivalent_spelling_and_prefers_exact()
    {
        string parent = Path.Combine(Path.GetTempPath(), "synthetic-parent");
        string candidate = Path.Combine(parent, "\u00c9");
        string rawIgnoreCase = Path.Combine(parent, "\u00e9");
        string normalizedOrdinal = Path.Combine(parent, "E\u0301");
        string normalizedIgnoreCase = Path.Combine(parent, "e\u0301");

        Assert.Multiple(() =>
        {
            Assert.That(
                FilePathComparison.SelectCanonicalExistingEntry(
                    "\u00c9",
                    candidate,
                    [normalizedIgnoreCase]),
                Is.EqualTo(normalizedIgnoreCase));
            Assert.That(
                FilePathComparison.SelectCanonicalExistingEntry(
                    "\u00c9",
                    candidate,
                    [normalizedOrdinal]),
                Is.EqualTo(normalizedOrdinal));
            Assert.That(
                FilePathComparison.SelectCanonicalExistingEntry(
                    "\u00c9",
                    candidate,
                    [rawIgnoreCase]),
                Is.EqualTo(rawIgnoreCase));
            Assert.That(
                FilePathComparison.SelectCanonicalExistingEntry(
                    "\u00c9",
                    candidate,
                    [rawIgnoreCase, candidate]),
                Is.EqualTo(candidate));
        });
    }

    [Test]
    public void Canonical_entry_selection_fails_closed_across_equivalence_categories()
    {
        string parent = Path.Combine(Path.GetTempPath(), "synthetic-parent");
        string candidate = Path.Combine(parent, "\u00c9");

        string selected = FilePathComparison.SelectCanonicalExistingEntry(
            "\u00c9",
            candidate,
            [
                Path.Combine(parent, "\u00e9"),
                Path.Combine(parent, "E\u0301"),
            ]);

        Assert.That(selected, Is.EqualTo(candidate));
    }

    [Test]
    public void Canonical_entry_selection_fails_closed_when_the_best_rank_is_ambiguous()
    {
        string parent = Path.Combine(Path.GetTempPath(), "synthetic-parent");
        string candidate = Path.Combine(parent, "CAF\u00c9");

        string selected = FilePathComparison.SelectCanonicalExistingEntry(
            "CAF\u00c9",
            candidate,
            [
                Path.Combine(parent, "caf\u00e9"),
                Path.Combine(parent, "Caf\u00e9"),
                Path.Combine(parent, "CAFE\u0301"),
            ]);

        Assert.That(selected, Is.EqualTo(candidate));
    }

    [Test]
    public void Canonical_entry_selection_tolerates_a_listing_that_repeats_a_name()
    {
        string parent = Path.Combine(Path.GetTempPath(), "synthetic-parent");
        string entry = Path.Combine(parent, "Templates");
        var entries = new FilePathComparison.ResolutionContext.DirectoryEntries([entry, entry]);

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select("Templates", entry), Is.EqualTo(entry));
            Assert.That(
                entries.Select("templates", Path.Combine(parent, "templates")),
                Is.EqualTo(entry));
        });
    }

    [Test]
    public void Containment_includes_the_root_and_rejects_a_sibling()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string child = Path.Combine(temporaryRoot, "nested", "item.json");
        string sibling = temporaryRoot + "-sibling";
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(child, "content");
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(temporaryRoot, temporaryRoot),
                    Is.True);
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(temporaryRoot, child),
                    Is.True);
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(temporaryRoot, sibling),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Test]
    public void Child_path_comparison_reports_the_actual_volume_result()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        const string storedName = "Metadata";
        const string alternateName = "mETADATA";
        Directory.CreateDirectory(Path.Combine(temporaryRoot, storedName));
        try
        {
            bool volumeResolvesAlternateCasing =
                Directory.Exists(Path.Combine(temporaryRoot, alternateName));

            Assert.That(
                FilePathComparison.TryAreSameChildPath(
                    temporaryRoot,
                    storedName,
                    alternateName,
                    out bool areSame),
                Is.True);
            Assert.That(areSame, Is.EqualTo(volumeResolvesAlternateCasing));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Child_path_comparison_reports_the_actual_volume_unicode_normalization_result()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        const string composedName = "caf\u00e9";
        const string decomposedName = "cafe\u0301";
        Directory.CreateDirectory(Path.Combine(temporaryRoot, composedName));
        try
        {
            bool volumeResolvesAlternateNormalization =
                Directory.Exists(Path.Combine(temporaryRoot, decomposedName));

            Assert.That(
                FilePathComparison.TryAreSameChildPath(
                    temporaryRoot,
                    composedName,
                    decomposedName,
                    out bool areSame),
                Is.True);
            Assert.That(areSame, Is.EqualTo(volumeResolvesAlternateNormalization));

            if (!volumeResolvesAlternateNormalization)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, decomposedName));
                Assert.That(
                    FilePathComparison.TryAreSameChildPath(
                        temporaryRoot,
                        composedName,
                        decomposedName,
                        out areSame),
                    Is.True);
                Assert.That(areSame, Is.False);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [TestCase(".")]
    [TestCase("..")]
    [TestCase("nested/item")]
    public void Child_path_comparison_rejects_non_child_names(string invalidName)
    {
        Assert.Throws<ArgumentException>(() =>
            FilePathComparison.TryAreSameChildPath(
                Path.GetTempPath(),
                invalidName,
                "item",
                out _));
    }

    [Test]
    public void Canonical_identity_resolves_symbolic_link_aliases()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string target = Path.Combine(temporaryRoot, "target");
        string alias = Path.Combine(temporaryRoot, "alias");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(alias, target);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
            }

            Assert.That(FilePathComparison.AreSameCanonicalPath(target, alias), Is.True);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Child_path_comparison_resolves_differently_named_symbolic_links()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string target = Path.Combine(temporaryRoot, ".git");
        string alias = Path.Combine(temporaryRoot, "metadata");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(alias, target);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
            }

            Assert.That(
                FilePathComparison.TryAreSameChildPath(
                    temporaryRoot,
                    "metadata",
                    ".git",
                    out bool areSame),
                Is.True);
            Assert.That(areSame, Is.True);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Canonical_identity_applies_parent_segments_after_resolving_symbolic_links()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string lexicalParent = Path.Combine(temporaryRoot, "lexical");
        string targetParent = Path.Combine(temporaryRoot, "outside");
        string targetLeaf = Path.Combine(temporaryRoot, "outside", "leaf");
        string alias = Path.Combine(lexicalParent, "alias");
        Directory.CreateDirectory(lexicalParent);
        Directory.CreateDirectory(targetLeaf);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(alias, targetLeaf);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
            }

            Assert.That(
                FilePathComparison.AreSameCanonicalPath(
                    Path.Combine(alias, ".."),
                    Path.Combine(targetLeaf, "..")),
                Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(
                    VersionControlPathComparison.ResolveCanonicalPath(Path.Combine(alias, "..")),
                    Is.EqualTo(FilePathComparison.ResolveCanonicalPath(targetParent)));
                Assert.That(
                    VersionControlPathComparison.IsSameOrDescendant(
                        lexicalParent, Path.Combine(alias, "..")),
                    Is.False);
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(
                        targetParent,
                        Path.Combine(alias, "..")),
                    Is.True);
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(
                        lexicalParent,
                        Path.Combine(alias, "..")),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void Windows_drive_root_casing_is_canonicalized()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Drive-letter casing is Windows-specific.");
        }

        string path = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetPathRoot(path)!;
        string alternateRoot = char.IsUpper(root[0])
            ? char.ToLowerInvariant(root[0]) + root[1..]
            : char.ToUpperInvariant(root[0]) + root[1..];
        string alternatePath = alternateRoot + path[root.Length..];

        Assert.That(FilePathComparison.AreSameCanonicalPath(path, alternatePath), Is.True);
    }

    [Test]
    [UnsupportedOSPlatform("windows")]
    public void Canonical_identity_fails_closed_beneath_a_traverse_only_ancestor()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string locked = Path.Combine(temporaryRoot, "Locked");
        string clip = Path.Combine(locked, "Media", "clip.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        File.WriteAllText(clip, "content");
        UnixFileMode? originalMode = null;
        try
        {
            originalMode = MakeTraverseOnly(locked);

            // Without a listing, a case or normalization alias cannot be told apart from a
            // distinct entry, so general identity queries keep reporting an error.
            Assert.Multiple(() =>
            {
                Assert.Throws<IOException>(() => FilePathComparison.ResolveCanonicalPath(clip));
                Assert.Throws<IOException>(
                    () => FilePathComparison.IsSameOrDescendant(temporaryRoot, clip));
            });
        }
        finally
        {
            RestoreMode(locked, originalMode);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    [UnsupportedOSPlatform("windows")]
    public void Root_containment_resolves_a_path_beneath_a_traverse_only_ancestor()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string owned = Path.Combine(temporaryRoot, "owned");
        string locked = Path.Combine(temporaryRoot, "Locked");
        string clip = Path.Combine(locked, "Media", "clip.bin");
        Directory.CreateDirectory(owned);
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        File.WriteAllText(clip, "content");
        string expectedIdentity = Path.Combine(
            FilePathComparison.ResolveCanonicalPath(temporaryRoot),
            "Locked",
            "Media",
            "clip.bin");
        UnixFileMode? originalMode = null;
        try
        {
            originalMode = MakeTraverseOnly(locked);
            var context = FilePathComparison.CreateResolutionContext();
            string ownedRoot = context.ResolveCanonicalPath(owned);
            string enclosingRoot = context.ResolveCanonicalPath(temporaryRoot);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.IsSameOrDescendantOfCanonicalRoot(ownedRoot, clip, out string outside),
                    Is.False);
                Assert.That(outside, Is.EqualTo(expectedIdentity));
                Assert.That(
                    context.IsSameOrDescendantOfCanonicalRoot(
                        enclosingRoot,
                        Path.Combine(locked, "Media", ".", "clip.bin"),
                        out string inside),
                    Is.True);
                Assert.That(inside, Is.EqualTo(expectedIdentity));
            });
        }
        finally
        {
            RestoreMode(locked, originalMode);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    [UnsupportedOSPlatform("windows")]
    public void Root_containment_follows_a_symbolic_link_beneath_a_traverse_only_ancestor_into_the_root()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string owned = Path.Combine(temporaryRoot, "owned");
        string resource = Path.Combine(owned, "resource.bin");
        string locked = Path.Combine(temporaryRoot, "Locked");
        string alias = Path.Combine(locked, "alias.bin");
        Directory.CreateDirectory(owned);
        Directory.CreateDirectory(locked);
        File.WriteAllText(resource, "content");
        UnixFileMode? originalMode = null;
        try
        {
            CreateSymbolicLinkOrIgnore(alias, resource);
            originalMode = MakeTraverseOnly(locked);
            var context = FilePathComparison.CreateResolutionContext();
            string ownedRoot = context.ResolveCanonicalPath(owned);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.IsSameOrDescendantOfCanonicalRoot(ownedRoot, alias, out string identity),
                    Is.True);
                Assert.That(identity, Is.EqualTo(Path.Combine(ownedRoot, "resource.bin")));
            });
        }
        finally
        {
            RestoreMode(locked, originalMode);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    [UnsupportedOSPlatform("windows")]
    public void Root_containment_rejects_a_symbolic_link_beneath_a_traverse_only_directory_that_escapes_the_root()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string owned = Path.Combine(temporaryRoot, "owned");
        string outside = Path.Combine(temporaryRoot, "outside", "escaped.bin");
        string locked = Path.Combine(owned, "Locked");
        string alias = Path.Combine(locked, "escaped.bin");
        string kept = Path.Combine(locked, "kept.bin");
        Directory.CreateDirectory(locked);
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "content");
        File.WriteAllText(kept, "content");
        UnixFileMode? originalMode = null;
        try
        {
            CreateSymbolicLinkOrIgnore(alias, outside);
            string outsideIdentity = FilePathComparison.ResolveCanonicalPath(outside);
            originalMode = MakeTraverseOnly(locked);
            var context = FilePathComparison.CreateResolutionContext();
            string ownedRoot = context.ResolveCanonicalPath(owned);

            Assert.Multiple(() =>
            {
                Assert.That(
                    context.IsSameOrDescendantOfCanonicalRoot(ownedRoot, alias, out string escaped),
                    Is.False);
                Assert.That(escaped, Is.EqualTo(outsideIdentity));
                Assert.That(
                    context.IsSameOrDescendantOfCanonicalRoot(ownedRoot, kept, out string inside),
                    Is.True);
                Assert.That(inside, Is.EqualTo(Path.Combine(ownedRoot, "Locked", "kept.bin")));
            });
        }
        finally
        {
            RestoreMode(locked, originalMode);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    [UnsupportedOSPlatform("windows")]
    public void A_listing_denied_to_a_containment_lookup_still_fails_a_strict_resolution()
    {
        string temporaryRoot = CreateTemporaryDirectory();
        string locked = Path.Combine(temporaryRoot, "Locked");
        string nestedRoot = Path.Combine(locked, "owned");
        string clip = Path.Combine(nestedRoot, "clip.bin");
        Directory.CreateDirectory(nestedRoot);
        File.WriteAllText(clip, "content");
        UnixFileMode? originalMode = null;
        try
        {
            originalMode = MakeTraverseOnly(locked);
            var context = FilePathComparison.CreateResolutionContext();
            string enclosingRoot = context.ResolveCanonicalPath(temporaryRoot);

            Assert.That(
                context.IsSameOrDescendantOfCanonicalRoot(enclosingRoot, clip, out _),
                Is.True);
            // A root beneath the unlisted directory could be spelled another way, so the cached
            // denial must not let it resolve.
            Assert.Throws<IOException>(() => context.ResolveCanonicalPath(nestedRoot));
        }
        finally
        {
            RestoreMode(locked, originalMode);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    // Grants traverse (execute) permission only, so entries can be reached by name but the
    // directory cannot be listed. Ignores the test where the permission is not enforced,
    // such as on Windows or when running as root.
    [UnsupportedOSPlatform("windows")]
    private static UnixFileMode MakeTraverseOnly(string directory)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Unix directory permissions are required.");
        UnixFileMode original = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserExecute);
        try
        {
            Directory.GetFileSystemEntries(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return original;
        }

        File.SetUnixFileMode(directory, original);
        Assert.Ignore("Directory listing permissions are not enforced here.");
        return original;
    }

    [UnsupportedOSPlatform("windows")]
    private static void RestoreMode(string directory, UnixFileMode? original)
    {
        if (original is { } mode) File.SetUnixFileMode(directory, mode);
    }

    private static void CreateSymbolicLinkOrIgnore(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable here: {ex.Message}");
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"beutl-path-comparison-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
