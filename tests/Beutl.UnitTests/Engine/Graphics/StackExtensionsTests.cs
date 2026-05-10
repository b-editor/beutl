using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

public class StackExtensionsTests
{
    [Test]
    public void PeekOrDefault_NonEmptyStack_ReturnsTop()
    {
        var stack = new Stack<int>();
        stack.Push(1);
        stack.Push(2);

        int top = stack.PeekOrDefault(-1);
        Assert.Multiple(() =>
        {
            Assert.That(top, Is.EqualTo(2));
            Assert.That(stack.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void PeekOrDefault_EmptyStack_ReturnsDefault()
    {
        var stack = new Stack<int>();

        int top = stack.PeekOrDefault(42);
        Assert.That(top, Is.EqualTo(42));
    }

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

    [Test]
    public void PeekOrDefault_NullableReference_ReturnsTopWhenPushed()
    {
        var stack = new Stack<string?>();
        stack.Push("hello");

        string? top = stack.PeekOrDefault(null);
        Assert.That(top, Is.EqualTo("hello"));
    }
}
