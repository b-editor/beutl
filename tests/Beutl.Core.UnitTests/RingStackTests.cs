using NUnit.Framework;

namespace Beutl.Core.UnitTests;

public class RingStackTests
{
    [Test]
    public void Push_AddsItemToStack()
    {
        // Arrange
        var stack = new RingStack<int>(3);

        // Act
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        // Assert
        Assert.AreEqual(3, stack.Count);
        Assert.AreEqual(3, stack.Pop());
        Assert.AreEqual(2, stack.Pop());
        Assert.AreEqual(1, stack.Pop());
        Assert.True(stack.IsEmpty);
    }

    [Test]
    public void Pop_RemovesItemFromStack()
    {
        // Arrange
        var stack = new RingStack<string>(3);
        stack.Push("foo");
        stack.Push("bar");

        // Act
        string item = stack.Pop();

        // Assert
        Assert.AreEqual("bar", item);
        Assert.AreEqual(1, stack.Count);
        Assert.False(stack.IsEmpty);
    }

    [Test]
    public void Pop_ThrowsExceptionIfStackIsEmpty()
    {
        // Arrange
        var stack = new RingStack<object>(1);

        // Act & Assert
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => stack.Pop());
        Assert.AreEqual("Stack underflow", ex?.Message);
    }

    [Test]
    public void Peek_ReturnsTopItemWithoutRemovingIt()
    {
        // Arrange
        var stack = new RingStack<int>(2);
        stack.Push(1);
        stack.Push(2);

        // Act
        int item = stack.Peek();

        // Assert
        Assert.AreEqual(2, item);
        Assert.AreEqual(2, stack.Count);
        Assert.False(stack.IsEmpty);
    }

    [Test]
    public void Peek_ThrowsExceptionIfStackIsEmpty()
    {
        // Arrange
        var stack = new RingStack<object>(1);

        // Act & Assert
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => stack.Peek());
        Assert.AreEqual("Stack is empty", ex?.Message);
    }

    [Test]
    public void IsEmpty_ReturnsTrueIfStackIsEmpty()
    {
        // Arrange
        var stack = new RingStack<double>(2);

        // Act & Assert
        Assert.True(stack.IsEmpty);
        stack.Push(1.23);
        Assert.False(stack.IsEmpty);
        stack.Pop();
        Assert.True(stack.IsEmpty);
    }

    [Test]
    public void IsFull_ReturnsTrueIfStackIsFull()
    {
        // Arrange
        var stack = new RingStack<string>(2);
        stack.Push("foo");

        // Act & Assert
        Assert.False(stack.IsFull);
        stack.Push("bar");
        Assert.True(stack.IsFull);
    }

    [Test]
    public void Count_ReturnsNumberOfItemsInStack()
    {
        // Arrange
        var stack = new RingStack<char>(3);

        // Act & Assert
        Assert.AreEqual(0, stack.Count);
        stack.Push('a');
        Assert.AreEqual(1, stack.Count);
        stack.Push('b');
        Assert.AreEqual(2, stack.Count);
        stack.Push('c');
        Assert.AreEqual(3, stack.Count);
        stack.Pop();
        Assert.AreEqual(2, stack.Count);
    }
}
