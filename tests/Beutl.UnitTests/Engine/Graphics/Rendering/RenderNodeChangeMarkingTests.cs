using System.Diagnostics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class RenderNodeChangeMarkingTests
{
    private const int ClearAttempts = 4_000_000;

    [Test]
    public void ClearChanges_cannot_swallow_a_MarkChanged_that_lands_while_it_runs()
    {
        using var node = new ContainerRenderNode();
        int stop = 0;
        var marker = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
                node.MarkChanged();
        })
        { IsBackground = true, Name = "MarkChanged" };

        long swallowedAt = -1;
        long swallowedVersion = -1;
        int attempts = 0;
        var elapsed = Stopwatch.StartNew();
        marker.Start();
        try
        {
            for (; attempts < ClearAttempts; attempts++)
            {
                long observed = node.ChangeVersion;
                node.ClearChanges(observed);
                long current = node.ChangeVersion;
                if (current > observed && !node.HasChanges)
                {
                    swallowedAt = observed;
                    swallowedVersion = current;
                    break;
                }
            }
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            Assert.That(marker.Join(TimeSpan.FromSeconds(30)), Is.True, "the marking thread did not finish");
        }

        TestContext.Out.WriteLine(
            $"attempts={attempts} elapsed={elapsed.ElapsedMilliseconds}ms version={node.ChangeVersion}");
        Assert.That(
            swallowedAt, Is.EqualTo(-1),
            $"a clear holding version {swallowedAt} reported the node clean at version {swallowedVersion}: "
            + "the mark that raised it was swallowed");
    }
}
