using Beutl.Editor.Services;

namespace Beutl.UnitTests.Editor;

public class EditorServiceRegistryTests
{
    [Test]
    public void An_owned_service_is_created_once_and_disposed_with_the_registry()
    {
        int created = 0;
        var registry = new EditorServiceRegistry().AddOwned<IDisposable>(() =>
        {
            created++;
            return new Disposable();
        });

        object? first = registry.GetService(typeof(IDisposable));
        object? second = registry.GetService(typeof(IDisposable));
        registry.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(created, Is.EqualTo(1));
            Assert.That(((Disposable)first!).IsDisposed, Is.True);
        });
    }

    // A request names the registered type itself; a type it implements or derives from finds nothing.
    [Test]
    public void A_lookup_is_read_on_each_request_under_its_exact_type()
    {
        var current = new Disposable();
        var registry = new EditorServiceRegistry().Add<IDisposable>(() => current);

        object? before = registry.GetService(typeof(IDisposable));
        current = new Disposable();
        object? after = registry.GetService(typeof(IDisposable));
        registry.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.SameAs(current).And.Not.SameAs(before));
            Assert.That(registry.GetService(typeof(Disposable)), Is.Null);
            Assert.That(registry.GetService(typeof(object)), Is.Null);
            Assert.That(current.IsDisposed, Is.False, "A lookup is not the registry's to dispose.");
        });
    }

    private sealed class Disposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
