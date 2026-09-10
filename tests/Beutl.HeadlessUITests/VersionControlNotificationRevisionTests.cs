using System.Reflection;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class VersionControlNotificationRevisionTests
{
    [AvaloniaTest]
    public async Task QueuedActivationWarning_OnlyPublishesForItsOriginalRevision()
    {
        await TestReset.ResetShellAsync();
        await using var coordinator = new VersionControlCoordinator(new ProjectService(), new EditorService(new ExtensionProvider()));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo revision = typeof(VersionControlCoordinator).GetField("_latestActivationRevision", flags)!;
        MethodInfo publish = typeof(VersionControlCoordinator).GetMethod("PublishNotification", flags)!;
        long original = (long)revision.GetValue(coordinator)!;
        int published = 0;
        void QueueWarning(long expectedRevision)
        {
            using var queued = new ManualResetEventSlim();
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { publish.Invoke(coordinator, new object?[] { (Action)(() => published++), expectedRevision }); }
                catch (Exception ex) { error = ex; }
                finally { queued.Set(); }
            });
            thread.Start();
            // Do not use Task.Wait: Avalonia can pump its dispatcher during that wait.
            Assert.That(SpinWait.SpinUntil(() => queued.IsSet, TimeSpan.FromSeconds(5)), Is.True);
            thread.Join();
            Assert.That(error, Is.Null);
        }
        QueueWarning(original);
        revision.SetValue(coordinator, original + 1);
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.That(published, Is.Zero);

        // Cleanup may leave _activation null; a current revision still owns its warning.
        QueueWarning(original + 1);
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.That(published, Is.EqualTo(1));
    }
}
