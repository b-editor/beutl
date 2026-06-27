using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tests.Workspace;

public class DestructiveGuardTests
{
    [Test]
    public void EnsureOverwriteAllowed_RejectsExistingFileWithoutConfirmation()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.bep");
        File.WriteAllText(path, "existing");

        var guard = new DestructiveGuard();

        var ex = Assert.Throws<DestructiveIntentException>(() =>
            guard.EnsureOverwriteAllowed(path, confirmed: false));
        Assert.That(ex!.Code, Is.EqualTo(AgentToolkit.Common.ErrorCode.DestructiveIntent));
    }

    [Test]
    public void EnsureOverwriteAllowed_AllowsExistingFileWithConfirmation()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.bep");
        File.WriteAllText(path, "existing");

        var guard = new DestructiveGuard();

        Assert.DoesNotThrow(() => guard.EnsureOverwriteAllowed(path, confirmed: true));
    }

    [Test]
    public void EnsureDeleteAllowed_RejectsDeleteWithoutConfirmation()
    {
        var guard = new DestructiveGuard();

        var ex = Assert.Throws<DestructiveIntentException>(() =>
            guard.EnsureDeleteAllowed(confirmed: false, target: "element"));
        Assert.That(ex!.Code, Is.EqualTo(AgentToolkit.Common.ErrorCode.DestructiveIntent));
    }
}
