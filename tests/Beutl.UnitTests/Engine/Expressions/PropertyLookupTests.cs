using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class PropertyLookupTests
{
    [Test]
    public void Constructor_WithNullRoot_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PropertyLookup(null!));
    }

    [Test]
    public void TryGetPropertyValue_WithInvalidPathFormat_ShouldReturnFalse()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);

        // Act (path without dot separator)
        bool result = lookup.TryGetPropertyValue<double>("invalidpath", context, out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithNonGuidIdentifier_ShouldReturnFalse()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);

        // Act (path with non-GUID identifier)
        bool result = lookup.TryGetPropertyValue<double>("not-a-guid.PropertyName", context, out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithNonExistentObject_ShouldReturnFalse()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);
        var guid = Guid.NewGuid();

        // Act
        bool result = lookup.TryGetPropertyValue<double>($"{guid}.PropertyName", context, out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithGuidAndPropertyName_AndNonExistentObject_ShouldReturnFalse()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);
        var guid = Guid.NewGuid();

        // Act
        bool result = lookup.TryGetPropertyValue<double>(guid, "PropertyName", context, out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithBracedGuid_ShouldWork()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);
        var guid = Guid.NewGuid();

        // Act - Test with braced GUID format {guid}
        bool result = lookup.TryGetPropertyValue<double>($"{{{guid}}}.PropertyName", context, out var value);

        // Assert - Should be false because object doesn't exist, but should parse correctly
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryGetPropertyValue_WithEmptyPropertyName_ShouldReturnFalse()
    {
        // Arrange
        var root = new TestCoreObject();
        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup);
        var guid = Guid.NewGuid();

        // Act - Path with empty property name
        bool result = lookup.TryGetPropertyValue<double>($"{guid}.", context, out var value);

        // Assert - Returns false because the object is not found (GUID doesn't exist in the hierarchy)
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryGetPropertyValue_WithExistingEngineObject_ShouldReturnPropertyValue()
    {
        // Arrange - Create a root with a child EngineObject
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 42.0;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Get the property value using the child's GUID
        bool result = lookup.TryGetPropertyValue<double>($"{child.Id}.Value", context, out var value);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo(42.0));
    }

    [Test]
    public void TryGetPropertyValue_WithExistingEngineObject_UsingGuidOverload_ShouldReturnPropertyValue()
    {
        // Arrange - Create a root with a child EngineObject
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 99.5;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Get the property value using the GUID overload
        bool result = lookup.TryGetPropertyValue<double>(child.Id, "Value", context, out var value);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo(99.5));
    }

    [Test]
    public void TryGetPropertyValue_WithExistingEngineObject_PropertyNameCaseInsensitive_ShouldReturnPropertyValue()
    {
        // Arrange - Create a root with a child EngineObject
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 123.0;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Get the property with different casing (lowercase)
        bool result = lookup.TryGetPropertyValue<double>($"{child.Id}.value", context, out var value);

        // Assert - Should work because PropertyLookup uses OrdinalIgnoreCase comparison
        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo(123.0));
    }

    [Test]
    public void TryGetPropertyValue_WithExistingEngineObject_NonExistentProperty_ShouldReturnFalse()
    {
        // Arrange - Create a root with a child EngineObject
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 42.0;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Try to get a non-existent property
        bool result = lookup.TryGetPropertyValue<double>($"{child.Id}.NonExistent", context, out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithRootObject_ShouldReturnPropertyValue()
    {
        // Arrange - Use the root object directly (includeSelf = true in FindById)
        var root = new TestEngineObject();
        root.Value.CurrentValue = 77.7;

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Get property from root itself
        bool result = lookup.TryGetPropertyValue<double>($"{root.Id}.Value", context, out var value);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo(77.7));
    }

    [Test]
    public void TryGetPropertyValue_WithDeepNestedObject_ShouldReturnPropertyValue()
    {
        // Arrange - Create a deep hierarchy: root -> child -> grandchild
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        var grandchild = new TestEngineObject();
        grandchild.Value.CurrentValue = 999.0;

        root.AddChild(child);
        child.AddChild(grandchild);

        var lookup = new PropertyLookup(root);
        var context = CreateContext(lookup, TimeSpan.Zero);

        // Act - Get property from deeply nested object
        bool result = lookup.TryGetPropertyValue<double>($"{grandchild.Id}.Value", context, out var value);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo(999.0));
    }

    private ExpressionContext CreateContext(PropertyLookup lookup)
    {
        return CreateContext(lookup, TimeSpan.Zero);
    }

    private ExpressionContext CreateContext(PropertyLookup lookup, TimeSpan time)
    {
        var property = Property.Create(0.0);
        return new ExpressionContext(time, property, lookup);
    }
}
