using Beutl.Services;

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
    public void PersistOrRollback_RollbackThrows_ThrowsDivergedWithOriginalPreserved()
    {
        var persistEx = new InvalidOperationException("disk full");
        var rollbackEx = new IOException("rollback failed too");

        ProjectStateDivergedException? caught = Assert.Throws<ProjectStateDivergedException>(() =>
            ProjectPersistence.PersistOrRollback(
                () => throw persistEx,
                () => throw rollbackEx));

        // The divergent state is surfaced as a distinct exception so callers can warn the user,
        // while the original persist failure and the rollback failure are both preserved.
        Assert.That(caught!.InnerException, Is.SameAs(persistEx));
        Assert.That(caught.RollbackException, Is.SameAs(rollbackEx));
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
