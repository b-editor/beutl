using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class StorageWriteTransactionTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-storage-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    [Test]
    public void Commit_KeepsEveryReplacementAndCreatedFile()
    {
        string existing = CreateFile("existing.json", "before");
        string created = PathOf("created.json");

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Replace(existing, "after");
            Replace(created, "new");
            transaction.Commit();
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existing), Is.EqualTo("after"));
            Assert.That(File.ReadAllText(created), Is.EqualTo("new"));
            Assert.That(StorageWriteTransaction.Current, Is.Null);
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public void Rollback_RestoresReplacedBytesAndRemovesCreatedFiles()
    {
        string existing = CreateFile("existing.json", "before");
        string created = PathOf("nested", "created.json");
        bool restored;

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Replace(existing, "first");
            Replace(existing, "second");
            Directory.CreateDirectory(Path.GetDirectoryName(created)!);
            Replace(created, "new", overwrite: false);
            Replace(created, "newer");
            restored = transaction.Rollback();
        }

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.True);
            Assert.That(File.ReadAllText(existing), Is.EqualTo("before"));
            Assert.That(File.Exists(created), Is.False);
            Assert.That(StorageWriteTransaction.Current, Is.Null);
            Assert.That(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public void Rollback_RestoresTheCompatibilityGateAfterEveryOtherFile()
    {
        string gate = CreateFile("project.bep", "gate-before");
        string first = CreateFile("first.belm", "first-before");
        string second = CreateFile("second.belm", "second-before");
        var restorations = new List<string>();
        using IDisposable faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Restore)
                restorations.Add(path);
        });

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Replace(first, "first-after");
            Replace(gate, "gate-after", isCompatibilityGate: true);
            Replace(second, "second-after");
            Assert.That(transaction.Rollback(), Is.True);
        }

        Assert.Multiple(() =>
        {
            // Newest first, except the gate, which waits for every other file.
            Assert.That(restorations, Is.EqualTo(new[] { second, first, gate }));
            Assert.That(File.ReadAllText(gate), Is.EqualTo("gate-before"));
            Assert.That(File.ReadAllText(first), Is.EqualTo("first-before"));
            Assert.That(File.ReadAllText(second), Is.EqualTo("second-before"));
        });
    }

    [Test]
    public void Rollback_RetainsTheCompatibilityGateWhenAnotherFileCannotBeRestored()
    {
        string gate = CreateFile("project.bep", "gate-before");
        string unrestorable = CreateFile("first.belm", "first-before");
        string restorable = CreateFile("second.belm", "second-before");
        string created = PathOf("third.belm");
        using IDisposable faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Restore && path == unrestorable)
                throw new IOException("The sidecar is locked.");
        });

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Replace(gate, "gate-after", isCompatibilityGate: true);
            Replace(unrestorable, "first-after");
            Replace(restorable, "second-after");
            Replace(created, "third");
            Assert.That(transaction.Rollback(), Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(gate), Is.EqualTo("gate-after"));
            Assert.That(File.ReadAllText(unrestorable), Is.EqualTo("first-after"));
            Assert.That(File.ReadAllText(restorable), Is.EqualTo("second-before"));
            Assert.That(File.Exists(created), Is.False);
        });
    }

    [Test]
    public void Rollback_ReportsACompatibilityGateThatCannotBeRestored()
    {
        string gate = CreateFile("project.bep", "gate-before");
        string sidecar = CreateFile("element.belm", "sidecar-before");
        using IDisposable faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Restore && path == gate)
                throw new IOException("The project file is locked.");
        });

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Replace(gate, "gate-after", isCompatibilityGate: true);
            Replace(sidecar, "sidecar-after");
            Assert.That(transaction.Rollback(), Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(gate), Is.EqualTo("gate-after"));
            Assert.That(File.ReadAllText(sidecar), Is.EqualTo("sidecar-before"));
        });
    }

    [Test]
    public void StoreToUri_TreatsAProjectFileAsTheCompatibilityGate()
    {
        string projectPath = CreateFile("project.bep", "project-before");
        string sidecar = CreateFile("element.belm", "sidecar-before");
        var project = new Project { Uri = new Uri(projectPath) };
        using IDisposable faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Restore && path == sidecar)
                throw new IOException("The sidecar is locked.");
        });

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            CoreSerializer.StoreToUri(project, project.Uri, CoreSerializationMode.Write);
            Replace(sidecar, "sidecar-after");
            Assert.That(transaction.Rollback(), Is.False);
        }

        Assert.That(File.ReadAllText(projectPath), Is.Not.EqualTo("project-before"));
    }

    [Test]
    public void FailedMoves_AreNotJournaled()
    {
        string collision = CreateFile("collision.belm", "theirs");
        string injected = CreateFile("injected.belm", "before");
        using IDisposable faults = StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
        {
            if (step == StorageWriteStep.Replace && path == injected)
                throw new IOException("The disk is full.");
        });

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Assert.Throws<IOException>(() => Replace(collision, "ours", overwrite: false));
            Assert.Throws<IOException>(() => Replace(injected, "after"));
            Assert.That(transaction.Rollback(), Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(collision), Is.EqualTo("theirs"));
            Assert.That(File.ReadAllText(injected), Is.EqualTo("before"));
        });
    }

    [Test]
    public void Rollback_RunsCompensationsNewestFirst()
    {
        var calls = new List<string>();

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            transaction.OnRollback(() => calls.Add("first"));
            transaction.OnRollback(() => throw new InvalidOperationException("Compensation failed."));
            transaction.OnRollback(() => calls.Add("third"));
            transaction.Rollback();
        }

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            transaction.OnRollback(() => calls.Add("committed"));
            transaction.Commit();
        }

        Assert.That(calls, Is.EqualTo(new[] { "third", "first" }));
    }

    [Test]
    public void ParallelWorkers_JournalIntoTheTransactionOfTheirFlow()
    {
        string[] paths = Enumerable.Range(0, 32)
            .Select(index => CreateFile($"element-{index}.belm", $"before-{index}"))
            .ToArray();

        using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
        {
            Parallel.ForEach(
                paths,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                path => Replace(path, "after"));
            Assert.That(transaction.Rollback(), Is.True);
        }

        Assert.That(
            paths.Select(File.ReadAllText),
            Is.EqualTo(Enumerable.Range(0, 32).Select(index => $"before-{index}")));
    }

    [Test]
    public void Dispose_WithoutCommit_RollsBack()
    {
        string existing = CreateFile("existing.json", "before");

        using (StorageWriteTransaction.Begin())
        {
            Replace(existing, "after");
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(existing), Is.EqualTo("before"));
            Assert.That(StorageWriteTransaction.Current, Is.Null);
        });
    }

    [Test]
    public void Begin_WhileATransactionIsActive_Throws()
    {
        using StorageWriteTransaction transaction = StorageWriteTransaction.Begin();

        Assert.Throws<InvalidOperationException>(() => StorageWriteTransaction.Begin());
    }

    [Test]
    public void CompletedTransaction_RejectsFurtherWork()
    {
        StorageWriteTransaction transaction = StorageWriteTransaction.Begin();
        transaction.Commit();

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => transaction.OnRollback(() => { }));
            Assert.Throws<InvalidOperationException>(() => transaction.Rollback());
            Assert.DoesNotThrow(transaction.Dispose);
        });
    }

    private string PathOf(params string[] parts) => Path.Combine([_directory, .. parts]);

    private string CreateFile(string name, string contents)
    {
        string path = PathOf(name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void Replace(
        string path,
        string contents,
        bool overwrite = true,
        bool isCompatibilityGate = false)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, contents);
        try
        {
            StorageWriteTransaction.MoveIntoPlace(temporary, path, overwrite, isCompatibilityGate);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
