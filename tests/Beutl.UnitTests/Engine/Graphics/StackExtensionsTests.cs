using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

public class StackExtensionsTests
{
    [Test]
    public void PopOrDefault_NonEmptyStack_RemovesAndReturnsTop()
    {
        var stack = new Stack<string>();
        stack.Push("a");
        stack.Push("b");

        string top = stack.PopOrDefault("default");
        Assert.Multiple(() =>
        {
            Assert.That(top, Is.EqualTo("b"));
            Assert.That(stack.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void PopOrDefault_EmptyStack_ReturnsDefault()
    {
        var stack = new Stack<int>();
        int popped = stack.PopOrDefault(99);

        Assert.Multiple(() =>
        {
            Assert.That(popped, Is.EqualTo(99));
            Assert.That(stack.Count, Is.EqualTo(0));
        });
    }
}
