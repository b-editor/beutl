using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class UnloadDiagnosticsDumpRetentionTests
{
    private string _logDir = null!;

    [SetUp]
    public void SetUp()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "beutl-unload-dump-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_logDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_logDir))
        {
            Directory.Delete(_logDir, recursive: true);
        }
    }

    [Test]
    public void PruneOldDumps_KeepsNewestByWriteTime_AndLeavesUnrelatedFiles()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Package name is deliberately the inverse of the age, so a filename sort would retain the opposite set from
        // the write-time sort the pruner must use.
        for (int i = 0; i < 8; i++)
        {
            string path = Path.Combine(_logDir, $"unload-dump-Pkg{7 - i}-2026010100000{i}.txt");
            File.WriteAllText(path, "dump");
            File.SetLastWriteTimeUtc(path, baseTime.AddMinutes(i));
        }

        string unrelated = Path.Combine(_logDir, "log20260101000000-4321.txt");
        File.WriteAllText(unrelated, "keep");

        ClrmdLoadContextUnloadDiagnostics.PruneOldDumps(_logDir, maxRetained: 5);

        string[] remaining = Directory.GetFiles(_logDir, "unload-dump-*.txt")
            .Select(Path.GetFileName)
            .ToArray()!;
        Assert.Multiple(() =>
        {
            Assert.That(remaining, Has.Length.EqualTo(5));
            // The newest by write time (index 7) survives and the oldest (index 0) is gone, regardless of package name.
            Assert.That(remaining, Does.Contain("unload-dump-Pkg0-20260101000007.txt"));
            Assert.That(remaining, Does.Not.Contain("unload-dump-Pkg7-20260101000000.txt"));
            Assert.That(File.Exists(unrelated), Is.True);
        });
    }

    [Test]
    public void PruneOldDumps_DoesNothing_WhenAtOrBelowLimit()
    {
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_logDir, $"unload-dump-Pkg-2026010100000{i}.txt"), "dump");
        }

        ClrmdLoadContextUnloadDiagnostics.PruneOldDumps(_logDir, maxRetained: 5);

        Assert.That(Directory.GetFiles(_logDir, "unload-dump-*.txt"), Has.Length.EqualTo(3));
    }

    [Test]
    public void PruneOldDumps_BreaksWriteTimeTies_ByNameDeterministically()
    {
        var sameTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (char c = 'a'; c <= 'f'; c++)
        {
            string path = Path.Combine(_logDir, $"unload-dump-Pkg-20260101000000-{c}.txt");
            File.WriteAllText(path, "dump");
            File.SetLastWriteTimeUtc(path, sameTime);
        }

        ClrmdLoadContextUnloadDiagnostics.PruneOldDumps(_logDir, maxRetained: 2);

        string[] remaining = Directory.GetFiles(_logDir, "unload-dump-*.txt")
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray()!;
        // With write times tied, the highest names win, so the two kept files are deterministic.
        Assert.That(remaining, Is.EqualTo(new[]
        {
            "unload-dump-Pkg-20260101000000-e.txt",
            "unload-dump-Pkg-20260101000000-f.txt",
        }));
    }

    [Test]
    public void PruneOldDumps_DoesNotThrow_WhenDirectoryMissing()
    {
        string missing = Path.Combine(_logDir, "does-not-exist");

        Assert.That(() => ClrmdLoadContextUnloadDiagnostics.PruneOldDumps(missing, maxRetained: 5), Throws.Nothing);
    }
}
