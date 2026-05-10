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
    public void ParameterizedConstructor_StoresValues()
    {
        var popable = new FakePopable();
        var state = new PushedState(popable, level: 7);

        Assert.Multiple(() =>
        {
            Assert.That(state.Popable, Is.SameAs(popable));
            Assert.That(state.Count, Is.EqualTo(7));
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

    [Test]
    public void RecordEquality_ComparesByValue()
    {
        var popable = new FakePopable();
        var a = new PushedState(popable, 5);
        var b = new PushedState(popable, 5);

        Assert.That(a, Is.EqualTo(b));
    }
}
