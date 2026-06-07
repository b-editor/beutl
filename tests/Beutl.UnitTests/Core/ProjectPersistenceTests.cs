namespace Beutl.UnitTests.Core;

public class ProjectPersistenceTests
{
    [Test]
    public void PersistOrRollback_PersistSucceeds_RollbackNotCalled()
    {
        bool persisted = false;
        bool rolledBack = false;

        ProjectPersistence.PersistOrRollback(
            () => persisted = true,
            () => rolledBack = true);

        Assert.That(persisted, Is.True);
        Assert.That(rolledBack, Is.False);
    }

    [Test]
    public void PersistOrRollback_PersistThrows_RollbackCalledAndOriginalRethrown()
    {
        bool rolledBack = false;
        var thrown = new InvalidOperationException("disk full");

        InvalidOperationException? caught = Assert.Throws<InvalidOperationException>(() =>
            ProjectPersistence.PersistOrRollback(
                () => throw thrown,
                () => rolledBack = true));

        Assert.That(rolledBack, Is.True);
        Assert.That(caught, Is.SameAs(thrown));
    }

    [Test]
    public void PersistOrRollback_NullPersist_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProjectPersistence.PersistOrRollback(null!, () => { }));
    }

    [Test]
    public void PersistOrRollback_NullRollback_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProjectPersistence.PersistOrRollback(() => { }, null!));
    }
}
