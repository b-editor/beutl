using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public sealed class AtomicFileExchangeTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-atomic-exchange-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void Windows_partial_exchange_failure_restores_the_displaced_target()
    {
        string target = Path.Combine(_directory, "config");
        string replacement = Path.Combine(_directory, "config.lock");
        string displaced = Path.Combine(_directory, ".config.displaced.tmp");
        File.WriteAllText(replacement, "replacement\n");
        File.WriteAllText(displaced, "original\n");

        Assert.Throws<IOException>(() => AtomicFileExchange.HandleWindowsExchangeFailure(
            target,
            replacement,
            displaced,
            error: 1177));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(target), Is.EqualTo("original\n"));
            Assert.That(File.ReadAllText(replacement), Is.EqualTo("replacement\n"));
            Assert.That(File.Exists(displaced), Is.False);
        });
    }

    [Test]
    public void Windows_partial_exchange_failure_retains_the_backup_when_restore_loses_a_race()
    {
        string target = Path.Combine(_directory, "config");
        string replacement = Path.Combine(_directory, "config.lock");
        string displaced = Path.Combine(_directory, ".config.displaced.tmp");
        File.WriteAllText(target, "later edit\n");
        File.WriteAllText(replacement, "replacement\n");
        File.WriteAllText(displaced, "original\n");

        AtomicFileExchangeException? exception = Assert.Throws<AtomicFileExchangeException>(() =>
            AtomicFileExchange.HandleWindowsExchangeFailure(
                target,
                replacement,
                displaced,
                error: 1177));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.NativeError, Is.EqualTo(1177));
            Assert.That(exception.DisplacedPath, Is.EqualTo(displaced));
            Assert.That(File.ReadAllText(target), Is.EqualTo("later edit\n"));
            Assert.That(File.ReadAllText(replacement), Is.EqualTo("replacement\n"));
            Assert.That(File.ReadAllText(displaced), Is.EqualTo("original\n"));
        });
    }
}
