using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

public class PushedStateTests
{
    private sealed class FakePopable : IPopable
    {
        public List<int> PopCalls { get; } = [];

        public void Pop(int count) => PopCalls.Add(count);
    }

    [Test]
    public void DefaultConstructor_HasMinusOneCount()
    {
        var state = new PushedState();
        Assert.Multiple(() =>
        {
            Assert.That(state.Popable, Is.Null);
            Assert.That(state.Count, Is.EqualTo(-1));
        });
    }

    [Test]
    public void Dispose_CallsPopWithStoredCount()
    {
        var popable = new FakePopable();
        using (new PushedState(popable, 3))
        {
        }

        Assert.That(popable.PopCalls, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Dispose_OnDefaultState_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            using var _ = new PushedState();
        });
    }
}
