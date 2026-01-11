using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.NodeTree;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class CollectionChangeOperationTests
{
    private OperationExecutionContext _context = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        var root = new TestCoreObjectWithList();
        _context = new OperationExecutionContext(root);
    }

    #region InsertCollectionItemOperation Tests

    [Test]
    public void InsertCollectionItemOperation_ApplyTo_ShouldInsertItemAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C"]);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(4));
        Assert.That(owner.Items[1], Is.EqualTo("X"));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "X", "B", "C" }));
    }

    [Test]
    public void InsertCollectionItemOperation_ApplyTo_WhenIndexNegative_ShouldAddToEnd()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = -1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(3));
        Assert.That(owner.Items[2], Is.EqualTo("X"));
    }

    [Test]
    public void InsertCollectionItemOperation_ApplyTo_WhenIndexGreaterThanCount_ShouldAddToEnd()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 100,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(3));
        Assert.That(owner.Items[2], Is.EqualTo("X"));
    }

    [Test]
    public void InsertCollectionItemOperation_RevertTo_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "X", "B", "C"]);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(3));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void InsertCollectionItemOperation_Items_ShouldReturnSingleItem()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var items = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(items, Is.EqualTo(new object?[] { "X" }));
    }

    [Test]
    public void InsertCollectionItemOperation_ApplyToEngineProperty_ShouldInsertItemAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(3));
        Assert.That(owner.Items[1], Is.EqualTo("X"));
    }

    [Test]
    public void InsertCollectionItemOperation_ApplyToEngineProperty_WhenIndexOutOfRange_ShouldAddToEnd()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = -1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(2));
        Assert.That(owner.Items[1], Is.EqualTo("X"));
    }

    [Test]
    public void InsertCollectionItemOperation_RevertToEngineProperty_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("X");
        owner.Items.Add("B");

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(2));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
    }

    #endregion

    #region InsertCollectionRangeOperation Tests

    [Test]
    public void InsertCollectionRangeOperation_ApplyTo_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "D"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(4));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyTo_WhenIndexNegative_ShouldAddToEnd()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["X", "Y"],
            Index = -1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(4));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "X", "Y" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyTo_WhenIndexGreaterThanCount_ShouldAddToEnd()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["X", "Y"],
            Index = 100,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(4));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "X", "Y" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_RevertTo_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D"]);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(2));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "D" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_Items_ShouldReturnAllItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var items = new[] { "X", "Y", "Z" };
        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = items,
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var opItems = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(opItems, Is.EqualTo(items.Cast<object?>()));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyToEngineProperty_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("D");

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(4));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
        Assert.That(owner.Items[2], Is.EqualTo("C"));
        Assert.That(owner.Items[3], Is.EqualTo("D"));
    }

    [Test]
    public void InsertCollectionRangeOperation_RevertToEngineProperty_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");
        owner.Items.Add("C");
        owner.Items.Add("D");

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(2));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("D"));
    }

    #endregion

    #region RemoveCollectionItemOperation Tests

    [Test]
    public void RemoveCollectionItemOperation_ApplyTo_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C"]);

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(2));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "C" }));
    }

    [Test]
    public void RemoveCollectionItemOperation_RevertTo_ShouldInsertItemAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "C"]);

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(3));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void RemoveCollectionItemOperation_Items_ShouldReturnSingleItem()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var items = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(items, Is.EqualTo(new object?[] { "X" }));
    }

    [Test]
    public void RemoveCollectionItemOperation_ApplyToEngineProperty_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");
        owner.Items.Add("C");

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(2));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("C"));
    }

    [Test]
    public void RemoveCollectionItemOperation_RevertToEngineProperty_ShouldInsertItemAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("C");

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(3));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
        Assert.That(owner.Items[2], Is.EqualTo("C"));
    }

    #endregion

    #region RemoveCollectionRangeOperation Tests

    [Test]
    public void RemoveCollectionRangeOperation_ApplyTo_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D"]);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(2));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "D" }));
    }

    [Test]
    public void RemoveCollectionRangeOperation_RevertTo_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "D"]);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Has.Count.EqualTo(4));
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void RemoveCollectionRangeOperation_Items_ShouldReturnAllItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var items = new[] { "X", "Y", "Z" };
        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = items,
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var opItems = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(opItems, Is.EqualTo(items.Cast<object?>()));
    }

    [Test]
    public void RemoveCollectionRangeOperation_ApplyToEngineProperty_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");
        owner.Items.Add("C");
        owner.Items.Add("D");

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(2));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("D"));
    }

    [Test]
    public void RemoveCollectionRangeOperation_RevertToEngineProperty_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("D");

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items.Count, Is.EqualTo(4));
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
        Assert.That(owner.Items[2], Is.EqualTo("C"));
        Assert.That(owner.Items[3], Is.EqualTo("D"));
    }

    #endregion

    #region MoveCollectionItemOperation Tests

    [Test]
    public void MoveCollectionItemOperation_ApplyTo_ShouldMoveItem()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D"]);

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Is.EqualTo(new[] { "B", "C", "A", "D" }));
    }

    [Test]
    public void MoveCollectionItemOperation_RevertTo_ShouldMoveItemBack()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["B", "C", "A", "D"]);

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void MoveCollectionItemOperation_Items_ShouldReturnEmptyCollection()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new MoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        var items = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(items, Is.Empty);
    }

    [Test]
    public void MoveCollectionItemOperation_ApplyToEngineProperty_ShouldMoveItem()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");
        owner.Items.Add("C");

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items[0], Is.EqualTo("B"));
        Assert.That(owner.Items[1], Is.EqualTo("C"));
        Assert.That(owner.Items[2], Is.EqualTo("A"));
    }

    [Test]
    public void MoveCollectionItemOperation_RevertToEngineProperty_ShouldMoveItemBack()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("B");
        owner.Items.Add("C");
        owner.Items.Add("A");

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
        Assert.That(owner.Items[2], Is.EqualTo("C"));
    }

    #endregion

    #region MoveCollectionRangeOperation Tests

    [Test]
    public void MoveCollectionRangeOperation_ApplyTo_ShouldMoveItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D", "E"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Is.EqualTo(new[] { "C", "D", "E", "A", "B" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_ApplyTo_WhenMovingBackward_ShouldMoveItems()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["A", "B", "C", "D", "E"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 3,
            NewIndex = 1,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "D", "E", "B", "C" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_RevertTo_ShouldMoveItemsBack()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        owner.Items.AddRange(["C", "D", "E", "A", "B"]);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items, Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_Items_ShouldReturnEmptyCollection()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        var items = ((ICollectionChangeOperation)operation).Items;

        // Assert
        Assert.That(items, Is.Empty);
    }

    [Test]
    public void MoveCollectionRangeOperation_ApplyToEngineProperty_ShouldMoveItems()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("A");
        owner.Items.Add("B");
        owner.Items.Add("C");
        owner.Items.Add("D");
        owner.Items.Add("E");

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(owner.Items[0], Is.EqualTo("C"));
        Assert.That(owner.Items[1], Is.EqualTo("D"));
        Assert.That(owner.Items[2], Is.EqualTo("E"));
        Assert.That(owner.Items[3], Is.EqualTo("A"));
        Assert.That(owner.Items[4], Is.EqualTo("B"));
    }

    [Test]
    public void MoveCollectionRangeOperation_RevertToEngineProperty_ShouldMoveItemsBack()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();
        owner.Items.Add("C");
        owner.Items.Add("D");
        owner.Items.Add("E");
        owner.Items.Add("A");
        owner.Items.Add("B");

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(owner.Items[0], Is.EqualTo("A"));
        Assert.That(owner.Items[1], Is.EqualTo("B"));
        Assert.That(owner.Items[2], Is.EqualTo("C"));
        Assert.That(owner.Items[3], Is.EqualTo("D"));
        Assert.That(owner.Items[4], Is.EqualTo("E"));
    }

    #endregion

    #region CollectionChangeOperation Base Class Tests

    [Test]
    public void CollectionChangeOperation_Apply_WhenPropertyNotList_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var owner = new TestCoreObjectWithScalarProperty();

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenPropertyNotFound_ShouldNotThrow()
    {
        // Arrange - Use EngineObject without the property
        var owner = new TestCoreObjectWithList();

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "NonExistentProperty",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act - Should not throw, just silently fail to find property
        Assert.DoesNotThrow(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_Revert_WhenPropertyNotList_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var owner = new TestCoreObjectWithScalarProperty();

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Revert(_context));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenEnginePropertyNotList_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var owner = new TestEngineObjectWithScalarProperty();

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Title",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenEnginePropertyNotFound_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var owner = new TestEngineObjectWithList();

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "NonExistentProperty",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_PropertyPath_ShouldBeAccessible()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var propertyPath = ((IPropertyPathProvider)operation).PropertyPath;

        // Assert
        Assert.That(propertyPath, Is.EqualTo("Items"));
    }

    [Test]
    public void CollectionChangeOperation_Object_ShouldBeAccessible()
    {
        // Arrange
        var owner = new TestCoreObjectWithList();
        var operation = new InsertCollectionItemOperation<string>
        {
            Object = owner,
            PropertyPath = "Items",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act
        var obj = ((ICollectionChangeOperation)operation).Object;

        // Assert
        Assert.That(obj, Is.SameAs(owner));
    }

    #endregion

    #region NodeItem Tests

    [Test]
    public void InsertCollectionItemOperation_ApplyToNodeItem_ShouldInsertItemAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(4));
        Assert.That(list[1], Is.EqualTo("X"));
        Assert.That(list, Is.EqualTo(new[] { "A", "X", "B", "C" }));
    }

    [Test]
    public void InsertCollectionItemOperation_RevertToNodeItem_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "X", "B", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "X",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(3));
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_ApplyToNodeItem_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(4));
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void InsertCollectionRangeOperation_RevertToNodeItem_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list, Is.EqualTo(new[] { "A", "D" }));
    }

    [Test]
    public void RemoveCollectionItemOperation_ApplyToNodeItem_ShouldRemoveItemAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list, Is.EqualTo(new[] { "A", "C" }));
    }

    [Test]
    public void RemoveCollectionItemOperation_RevertToNodeItem_ShouldInsertItemAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new RemoveCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "B",
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(3));
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void RemoveCollectionRangeOperation_ApplyToNodeItem_ShouldRemoveItemsAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list, Is.EqualTo(new[] { "A", "D" }));
    }

    [Test]
    public void RemoveCollectionRangeOperation_RevertToNodeItem_ShouldInsertItemsAtIndex()
    {
        // Arrange
        var list = new CoreList<string>(["A", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new RemoveCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Items = ["B", "C"],
            Index = 1,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Has.Count.EqualTo(4));
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void MoveCollectionItemOperation_ApplyToNodeItem_ShouldMoveItem()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Is.EqualTo(new[] { "B", "C", "A", "D" }));
    }

    [Test]
    public void MoveCollectionItemOperation_RevertToNodeItem_ShouldMoveItemBack()
    {
        // Arrange
        var list = new CoreList<string>(["B", "C", "A", "D"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new MoveCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            OldIndex = 0,
            NewIndex = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_ApplyToNodeItem_ShouldMoveItems()
    {
        // Arrange
        var list = new CoreList<string>(["A", "B", "C", "D", "E"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Apply(_context);

        // Assert
        Assert.That(list, Is.EqualTo(new[] { "C", "D", "E", "A", "B" }));
    }

    [Test]
    public void MoveCollectionRangeOperation_RevertToNodeItem_ShouldMoveItemsBack()
    {
        // Arrange
        var list = new CoreList<string>(["C", "D", "E", "A", "B"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new MoveCollectionRangeOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            OldIndex = 0,
            NewIndex = 3,
            Count = 2,
            SequenceNumber = 1
        };

        // Act
        operation.Revert(_context);

        // Assert
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeItemPropertyIsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        // Property is null (not set)

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeItemPropertyValueIsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", null, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "Property",
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => operation.Apply(_context));
    }

    [Test]
    public void CollectionChangeOperation_Apply_WhenNodeItemPropertyPathNotProperty_ShouldNotUseNodeItemPath()
    {
        // Arrange - NodeItem with PropertyPath != "Property" should fall through to CoreObject path
        var list = new CoreList<string>(["A", "B", "C"]);
        var adapter = new NodePropertyAdapter<CoreList<string>>("Property", list, null);
        var nodeItem = new DefaultNodeItem<CoreList<string>>();
        nodeItem.SetProperty(adapter);

        var operation = new InsertCollectionItemOperation<string>
        {
            Object = nodeItem,
            PropertyPath = "OtherProperty", // Not "Property"
            Item = "X",
            Index = 0,
            SequenceNumber = 1
        };

        // Act - Should not throw, just try CoreObject path (which won't find property)
        Assert.DoesNotThrow(() => operation.Apply(_context));

        // Assert - List should be unchanged because "OtherProperty" doesn't exist
        Assert.That(list, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    #endregion

    #region Test Helper Classes

    private class TestCoreObjectWithList : CoreObject
    {
        public static readonly CoreProperty<CoreList<string>> ItemsProperty;

        private CoreList<string> _items = [];

        static TestCoreObjectWithList()
        {
            ItemsProperty = ConfigureProperty<CoreList<string>, TestCoreObjectWithList>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<string> Items
        {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
    }

    private class TestCoreObjectWithScalarProperty : CoreObject
    {
        public static readonly CoreProperty<string> TitleProperty;

        private string _title = "";

        static TestCoreObjectWithScalarProperty()
        {
            TitleProperty = ConfigureProperty<string, TestCoreObjectWithScalarProperty>(nameof(Title))
                .Accessor(o => o.Title, (o, v) => o.Title = v)
                .Register();
        }

        public string Title
        {
            get => _title;
            set => SetAndRaise(TitleProperty, ref _title, value);
        }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithList : EngineObject
    {
        public TestEngineObjectWithList()
        {
            Items = new ListProperty<string>();
            ScanProperties<TestEngineObjectWithList>();
        }

        public ListProperty<string> Items { get; }
    }

    [SuppressResourceClassGeneration]
    private class TestEngineObjectWithScalarProperty : EngineObject
    {
        public TestEngineObjectWithScalarProperty()
        {
            Title = Property.Create("");
            ScanProperties<TestEngineObjectWithScalarProperty>();
        }

        public IProperty<string> Title { get; }
    }

    #endregion
}
