using System.Text.Json;

using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyFingerprintTests
{
    [Test]
    public void Equality_UsesPathSizeAndMtime()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        string sourcePath = CreatePath("source.mov");
        string otherPath = CreatePath("other.mov");
        var baseline = new ProxyFingerprint(sourcePath, 123, mtime);

        Assert.Multiple(() =>
        {
            Assert.That(new ProxyFingerprint(sourcePath, 123, mtime), Is.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(otherPath, 123, mtime), Is.Not.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(sourcePath, 456, mtime), Is.Not.EqualTo(baseline));
            Assert.That(new ProxyFingerprint(sourcePath, 123, mtime.AddTicks(1)), Is.Not.EqualTo(baseline));
        });
    }

    [Test]
    public void Normalization_CaseFolding_FollowsFilesystemCaseSensitivity()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var lower = new ProxyFingerprint(CreatePath("case.mov"), 123, mtime);
        var upper = new ProxyFingerprint(CreatePath("CASE.MOV"), 123, mtime);

        if (OperatingSystem.IsMacOS())
        {
            Assert.That(upper, Is.EqualTo(lower));
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.That(upper, Is.EqualTo(lower));
        }
        else
        {
            Assert.That(upper, Is.Not.EqualTo(lower));
        }
    }

    [Test]
    public void TryFromFile_MissingFile_ReturnsFalse()
    {
        string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".mov");

        bool result = ProxyFingerprint.TryFromFile(missing, out ProxyFingerprint fingerprint);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(fingerprint, Is.EqualTo(default(ProxyFingerprint)));
        });
    }

    [Test]
    public void FromFile_NormalizesToAbsolutePath()
    {
        string dir = TestContext.CurrentContext.WorkDirectory;
        string path = Path.Combine(dir, Guid.NewGuid() + ".mov");
        File.WriteAllBytes(path, [1, 2, 3]);

        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(path);

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(fingerprint.AbsolutePath), Is.True);
            Assert.That(fingerprint.FileSizeBytes, Is.EqualTo(3));
            Assert.That(fingerprint.MtimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void SourcePath_PreservesOriginalCasing_ForFilesystemIo()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        string original = CreatePath("Case.MOV");
        var fingerprint = new ProxyFingerprint(original, 123, mtime);

        Assert.Multiple(() =>
        {
            // SourcePath must stay usable for I/O; on macOS/Windows AbsolutePath is folded, so a
            // case-sensitive volume would fail to open it — SourcePath must NOT be folded.
            Assert.That(fingerprint.SourcePath, Is.EqualTo(Path.GetFullPath(original)));
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
            {
                Assert.That(fingerprint.AbsolutePath, Is.EqualTo(Path.GetFullPath(original).ToUpperInvariant()));
                Assert.That(fingerprint.SourcePath, Is.Not.EqualTo(fingerprint.AbsolutePath));
            }
        });
    }

    [Test]
    public void SourcePath_ExcludedFromEqualityAndHash()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var lower = new ProxyFingerprint(CreatePath("clip.mov"), 123, mtime);
        var mixed = new ProxyFingerprint(CreatePath("clip.mov"), 123, mtime) with { SourcePath = CreatePath("CLIP.MOV") };

        Assert.Multiple(() =>
        {
            Assert.That(mixed, Is.EqualTo(lower));
            Assert.That(mixed.GetHashCode(), Is.EqualTo(lower.GetHashCode()));
            Assert.That(mixed.SourcePath, Is.Not.EqualTo(lower.SourcePath));
        });
    }

    // Matches the store's serializer (ProxyStore.s_jsonOptions), which round-trips fingerprints.
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Test]
    public void JsonRoundTrip_PreservesSourcePath()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var fingerprint = new ProxyFingerprint(CreatePath("RoundTrip.MOV"), 123, mtime);

        string json = JsonSerializer.Serialize(fingerprint, s_jsonOptions);
        var restored = JsonSerializer.Deserialize<ProxyFingerprint>(json, s_jsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(fingerprint));
            Assert.That(restored.SourcePath, Is.EqualTo(fingerprint.SourcePath));
            Assert.That(restored.AbsolutePath, Is.EqualTo(fingerprint.AbsolutePath));
        });
    }

    [Test]
    public void Deserialize_LegacyWithoutSourcePath_FallsBackToAbsolutePath()
    {
        var mtime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var reference = new ProxyFingerprint(CreatePath("legacy.mov"), 123, mtime);
        string legacyJson = JsonSerializer.Serialize(
            new
            {
                absolutePath = reference.AbsolutePath,
                fileSizeBytes = reference.FileSizeBytes,
                mtimeUtc = reference.MtimeUtc,
            },
            s_jsonOptions);

        var restored = JsonSerializer.Deserialize<ProxyFingerprint>(legacyJson, s_jsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(reference));
            Assert.That(restored.SourcePath, Is.EqualTo(reference.AbsolutePath));
        });
    }

    [Test]
    public void ResolveComparableKey_SymlinkResolvesToSameKeyAsTarget()
    {
        string dir = TestContext.CurrentContext.WorkDirectory;
        string target = Path.Combine(dir, Guid.NewGuid() + ".mov");
        string link = Path.Combine(dir, Guid.NewGuid() + ".mov");
        File.WriteAllBytes(target, [1, 2, 3]);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not supported in this environment.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(ProxyFingerprint.ResolveComparableKey(link), Is.EqualTo(ProxyFingerprint.ResolveComparableKey(target)));
            Assert.That(ProxyFingerprint.ResolveComparableKey(link), Is.EqualTo(ProxyFingerprint.FromFile(target).AbsolutePath));
        });
    }

    // A symlink whose target moved/deleted must still resolve to the target key the entry was
    // registered under, so eviction's protected-source set and store change-matching find it.
    [Test]
    public void ResolveComparableKey_BrokenSymlink_ResolvesToMovedTargetKey()
    {
        string dir = TestContext.CurrentContext.WorkDirectory;
        string target = Path.Combine(dir, Guid.NewGuid() + ".mov");
        string link = Path.Combine(dir, Guid.NewGuid() + ".mov");
        File.WriteAllBytes(target, [1, 2, 3]);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Symbolic links are not supported in this environment.");
        }

        string targetKey = ProxyFingerprint.FromFile(link).AbsolutePath;
        File.Move(target, Path.Combine(dir, Guid.NewGuid() + ".moved"));

        Assert.That(ProxyFingerprint.ResolveComparableKey(link), Is.EqualTo(targetKey));
    }

    [Test]
    public void ResolveComparableKey_OfflineFile_FallsBackToNormalization()
    {
        string missing = CreatePath(Guid.NewGuid() + ".mov");

        Assert.That(
            ProxyFingerprint.ResolveComparableKey(missing),
            Is.EqualTo(ProxyFingerprint.NormalizeAbsolutePath(missing)));
    }

    // ForPathKey is a tracking-only fingerprint: it carries the comparable path key (for AbsolutePath
    // lookups) but leaves size/mtime unset, so it must NOT equal a real content fingerprint for the same
    // path — a real entry has a concrete size/mtime and would otherwise be mistaken for it.
    [Test]
    public void ForPathKey_CarriesComparableKey_ButDoesNotEqualContentFingerprint()
    {
        string missing = CreatePath(Guid.NewGuid() + ".mov");

        ProxyFingerprint key = ProxyFingerprint.ForPathKey(missing);

        Assert.Multiple(() =>
        {
            Assert.That(key.AbsolutePath, Is.EqualTo(ProxyFingerprint.ResolveComparableKey(missing)));
            Assert.That(key.FileSizeBytes, Is.EqualTo(0));
            Assert.That(key, Is.Not.EqualTo(new ProxyFingerprint(missing, 4096, DateTime.UtcNow)));
        });
    }

    private static string CreatePath(string fileName)
    {
        return Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
    }
}
