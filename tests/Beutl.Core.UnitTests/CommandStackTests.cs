using NUnit.Framework;

namespace Beutl.Core.UnitTests;

public class CommandStackTests
{
    [Test]
    public void Push_AddsItemToStack()
    {
        // Arrange
        var stack = new CommandStack<int>(3);

        // Act
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        // Assert
        Assert.That(stack.Count, Is.EqualTo(3));
        Assert.That(stack.Pop(), Is.EqualTo(3));
        Assert.That(stack.Pop(), Is.EqualTo(2));
        Assert.That(stack.Pop(), Is.EqualTo(1));
        Assert.That(stack.IsEmpty);
    }

    [Test]
    public void Pop_RemovesItemFromStack()
    {
        // Arrange
        var stack = new CommandStack<string>(3);
        stack.Push("foo");
        stack.Push("bar");

        // Act
        string? item = stack.Pop();

        // Assert
        Assert.That(item, Is.EqualTo("bar"));
        Assert.That(stack.Count, Is.EqualTo(1));
        Assert.That(!stack.IsEmpty);
    }

    [Test]
    public void Pop_ReturnsDefaultIfStackIsEmpty()
    {
        // Arrange
        var stack = new CommandStack<object>(1);

        // Act & Assert
        Assert.That(stack.Pop(), Is.Default);
    }

    [Test]
    public void Peek_ReturnsTopItemWithoutRemovingIt()
    {
        // Arrange
        var stack = new CommandStack<int>(2);
        stack.Push(1);
        stack.Push(2);

        // Act
        int item = stack.Peek();

        // Assert
        Assert.That(item, Is.EqualTo(2));
        Assert.That(stack.Count, Is.EqualTo(2));
        Assert.That(!stack.IsEmpty);
    }

    [Test]
    public void Peek_ReturnsDefaultIfStackIsEmpty()
    {
        // Arrange
        var stack = new CommandStack<object>(1);

        // Act & Assert
        Assert.That(stack.Peek(), Is.Default);
    }

    [Test]
    public void IsEmpty_ReturnsTrueIfStackIsEmpty()
    {
        // Arrange
        var stack = new CommandStack<double>(2);

        // Act & Assert
        Assert.That(stack.IsEmpty);
        stack.Push(1.23);
        Assert.That(!stack.IsEmpty);
        stack.Pop();
        Assert.That(stack.IsEmpty);
    }

    [Test]
    public void IsFull_ReturnsTrueIfStackIsFull()
    {
        // Arrange
        var stack = new CommandStack<string>(2);
        stack.Push("foo");

        // Act & Assert
        Assert.That(!stack.IsFull);
        stack.Push("bar");
        Assert.That(stack.IsFull);
    }

    [Test]
    public void Count_ReturnsNumberOfItemsInStack()
    {
        // Arrange
        var stack = new CommandStack<char>(3);

        // Act & Assert
        Assert.That(stack.Count, Is.EqualTo(0));
        stack.Push('a');
        Assert.That(stack.Count, Is.EqualTo(1));
        stack.Push('b');
        Assert.That(stack.Count, Is.EqualTo(2));
        stack.Push('c');
        Assert.That(stack.Count, Is.EqualTo(3));
        stack.Pop();
        Assert.That(stack.Count, Is.EqualTo(2));
    }
}
