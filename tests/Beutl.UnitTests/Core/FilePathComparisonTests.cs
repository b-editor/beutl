namespace Beutl.UnitTests.Core;

[TestFixture]
public class FilePathComparisonTests
{
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

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"beutl-path-comparison-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
