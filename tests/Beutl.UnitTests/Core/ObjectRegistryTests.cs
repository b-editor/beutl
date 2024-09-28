namespace Beutl.UnitTests.Core;

public class ObjectRegistryTests
{
    private class MyClass : CoreObject;

    [Test]
    public void Register_AddsObjectToRegistry()
    {
        var registry = new ObjectRegistry();
        var obj = new MyClass();

        registry.Register(obj);

        Assert.That(registry.Find(obj.Id), Is.EqualTo(obj));
    }

    [Test]
    public void Unregister_RemovesObjectFromRegistry()
    {
        var registry = new ObjectRegistry();
        var obj = new MyClass();
        registry.Register(obj);

        registry.Unregister(obj);

        Assert.That(registry.Find(obj.Id), Is.Null);
    }

    [Test]
    public void Find_ReturnsNullForNonExistentObject()
    {
        var registry = new ObjectRegistry();
        var id = Guid.NewGuid();

        var result = registry.Find(id);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Enumerate_ReturnsAllRegisteredObjects()
    {
        var registry = new ObjectRegistry();
        var obj1 = new MyClass();
        var obj2 = new MyClass();
        registry.Register(obj1);
        registry.Register(obj2);

        var objects = registry.Enumerate();

        Assert.That(objects, Does.Contain(obj1));
        Assert.That(objects, Does.Contain(obj2));
    }

    [Test]
    public void Resolve_ExecutesCallbackWhenObjectIsFound()
    {
        var registry = new ObjectRegistry();
        var obj = new MyClass();
        registry.Register(obj);
        bool callbackExecuted = false;

        registry.Resolve(obj.Id, this, (self, resolvedObj) => callbackExecuted = true);

        Assert.That(callbackExecuted, Is.True);
    }

    [Test]
    public void Resolve_AddsCallbackWhenObjectIsNotFound()
    {
        var registry = new ObjectRegistry();
        var id = Guid.NewGuid();
        bool callbackExecuted = false;

        registry.Resolve(id, this, (self, resolvedObj) => callbackExecuted = true);

        Assert.That(callbackExecuted, Is.False);
    }

    [Test]
    public void SetResolved_ExecutesPendingCallbacks()
    {
        var registry = new ObjectRegistry();
        var id = Guid.NewGuid();
        bool callbackExecuted = false;
        registry.Resolve(id, this, (self, resolvedObj) => callbackExecuted = true);

        // Register and change id to trigger callback
        var obj = new MyClass();
        registry.Register(obj);
        obj.Id = id;

        Assert.That(callbackExecuted, Is.True);
    }

    [Test]
    public void Object_IsRemovedFromRegistry_WhenGarbageCollected()
    {
        var registry = new ObjectRegistry();

        Guid id = CreateInstanceAndRegister();

        // Force garbage collection
        GC.Collect();
        GC.WaitForFullGCComplete();
        GC.WaitForPendingFinalizers();

        // Ensure the object is removed from the registry
        Assert.That(registry.Find(id), Is.Null);
        return;

        Guid CreateInstanceAndRegister()
        {
            var obj = new MyClass();
            registry.Register(obj);
            return obj.Id;
        }
    }
}
