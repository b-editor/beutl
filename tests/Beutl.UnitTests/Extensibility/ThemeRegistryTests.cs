using Avalonia.Styling;
using Beutl.Extensibility;

using NUnit.Framework;

namespace Beutl.UnitTests.Extensibility;

[TestFixture]
public class ThemeRegistryTests
{
    [TearDown]
    public void TearDown()
    {
        // ThemeRegistry is a static global; clear anything this test registered so tests do not leak.
        foreach (ThemeDescriptor descriptor in ThemeRegistry.Enumerate())
        {
            ThemeRegistry.Unregister(descriptor);
        }
    }

    [Test]
    public void Register_Resolve_Unregister()
    {
        var descriptor = new ThemeDescriptor("test.custom", "Custom", ThemeVariant.Dark);

        ThemeRegistry.Register(descriptor);

        Assert.That(ThemeRegistry.Resolve("test.custom"), Is.SameAs(descriptor));
        Assert.That(ThemeRegistry.Enumerate(), Contains.Item(descriptor));

        Assert.That(ThemeRegistry.Unregister(descriptor), Is.True);
        Assert.That(ThemeRegistry.Resolve("test.custom"), Is.Null);
        Assert.That(ThemeRegistry.Unregister(descriptor), Is.False);
    }

    [Test]
    public void Register_OverwritesSameId_AndIsIdempotent()
    {
        var first = new ThemeDescriptor("test.dup", "First", ThemeVariant.Dark);
        var second = new ThemeDescriptor("test.dup", "Second", ThemeVariant.Light);

        ThemeRegistry.Register(first);
        ThemeRegistry.Register(second);

        Assert.That(ThemeRegistry.Resolve("test.dup"), Is.SameAs(second));

        // Re-registering the same descriptor (re-Load after a rolled-back init) must not error.
        ThemeRegistry.Register(second);
        Assert.That(ThemeRegistry.Resolve("test.dup"), Is.SameAs(second));
    }

    [Test]
    public void Register_RejectsSystemFollowingWithResourceUri()
    {
        var descriptor = new ThemeDescriptor(
            "test.sys", "Sys", ThemeVariant.Default, new Uri("avares://test/x"), IsSystemFollowing: true);

        Assert.Throws<ArgumentException>(() => ThemeRegistry.Register(descriptor));
        Assert.That(ThemeRegistry.Resolve("test.sys"), Is.Null);
    }

    [Test]
    public void Register_KeepsExtension_AndGetExtension_ReturnsIt()
    {
        var ext = new TestThemeExtension("test.ext", "Ext");
        ext.Load();

        Assert.That(ThemeRegistry.GetExtension("test.ext"), Is.SameAs(ext));
        Assert.That(ThemeRegistry.GetExtension("nonexistent"), Is.Null);
        Assert.That(ext.Descriptor?.Id, Is.EqualTo("test.ext"));

        ext.Unload();
        Assert.That(ThemeRegistry.GetExtension("test.ext"), Is.Null);
    }

    [Test]
    public void NotifyApplied_CallsOnApplied_AndNotifyReverted_CallsOnReverted()
    {
        var ext = new TestThemeExtension("test.notify", "Notify");
        ext.Load();
        ThemeDescriptor descriptor = ThemeRegistry.Resolve("test.notify")!;

        ThemeNotifier.NotifyApplied(descriptor);
        Assert.That(ext.AppliedCount, Is.EqualTo(1));
        Assert.That(ext.LastApplied?.Id, Is.EqualTo("test.notify"));

        ThemeNotifier.NotifyReverted(descriptor);
        Assert.That(ext.RevertedCount, Is.EqualTo(1));

        // Built-in (no extension) must not throw.
        ThemeNotifier.NotifyApplied(new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark));
        ThemeNotifier.NotifyReverted(new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark));

        ext.Unload();
    }

    [Test]
    public void NotifyApplied_SwallowsExtensionException()
    {
        var ext = new ThrowingThemeExtension("test.throw", "Throw");
        ext.Load();
        ThemeDescriptor descriptor = ThemeRegistry.Resolve("test.throw")!;

        Assert.DoesNotThrow(() => ThemeNotifier.NotifyApplied(descriptor));
        Assert.DoesNotThrow(() => ThemeNotifier.NotifyReverted(descriptor));

        ext.Unload();
    }

    private sealed class TestThemeExtension : ThemeExtension
    {
        private readonly ThemeDescriptor _descriptor;
        public int AppliedCount;
        public int RevertedCount;
        public ThemeDescriptor? LastApplied;

        public TestThemeExtension(string id, string name)
        {
            _descriptor = new ThemeDescriptor(id, name, ThemeVariant.Dark);
        }

        public override ThemeDescriptor GetThemeDescriptor() => _descriptor;
        public override void OnApplied(ThemeApplyContext context) { AppliedCount++; LastApplied = context.Descriptor; }
        public override void OnReverted() => RevertedCount++;
    }

    private sealed class ThrowingThemeExtension : ThemeExtension
    {
        private readonly ThemeDescriptor _descriptor;
        public ThrowingThemeExtension(string id, string name) => _descriptor = new ThemeDescriptor(id, name, ThemeVariant.Dark);
        public override ThemeDescriptor GetThemeDescriptor() => _descriptor;
        public override void OnApplied(ThemeApplyContext context) => throw new InvalidOperationException("boom");
        public override void OnReverted() => throw new InvalidOperationException("boom");
    }

    [Test]
    public void ResolveOrDefault_FallsBackToDark_WhenIdUnknown()
    {
        var dark = new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark);
        ThemeRegistry.Register(dark);

        Assert.That(ThemeRegistry.ResolveOrDefault("nonexistent"), Is.SameAs(dark));
        Assert.That(ThemeRegistry.ResolveOrDefault(null), Is.SameAs(dark));
    }

    [Test]
    public void ResolveOrDefault_FallsBackToFirstNonSystem_WhenDarkMissing()
    {
        var custom = new ThemeDescriptor("custom.x", "X", ThemeVariant.Light);
        ThemeRegistry.Register(custom);

        Assert.That(ThemeRegistry.ResolveOrDefault("nonexistent"), Is.SameAs(custom));
    }

    [Test]
    public void ResolveOrDefault_ReturnsNull_WhenEmpty()
    {
        Assert.That(ThemeRegistry.ResolveOrDefault("anything"), Is.Null);
    }

    [Test]
    public void ResolveOrDefault_SkipsSystemFollowing_WhenDarkMissing()
    {
        var system = new ThemeDescriptor(BuiltinThemeIds.System, "System", ThemeVariant.Default, IsSystemFollowing: true);
        ThemeRegistry.Register(system);

        // System is the only theme, but it is system-following and must not be the fallback.
        Assert.That(ThemeRegistry.ResolveOrDefault("nonexistent"), Is.Null);
    }

    [Test]
    public void Changed_RaisedOnRegister_AndUnregister()
    {
        int calls = 0;
        ThemeRegistry.Changed += OnChanged;
        try
        {
            var descriptor = new ThemeDescriptor("test.changed", "C", ThemeVariant.Dark);
            calls = 0;
            ThemeRegistry.Register(descriptor);
            Assert.That(calls, Is.EqualTo(1));

            calls = 0;
            ThemeRegistry.Unregister(descriptor);
            Assert.That(calls, Is.EqualTo(1));

            // Unregistering a descriptor that was never registered must not raise.
            calls = 0;
            ThemeRegistry.Unregister(descriptor);
            Assert.That(calls, Is.EqualTo(0));
        }
        finally
        {
            ThemeRegistry.Changed -= OnChanged;
        }

        void OnChanged(object? sender, EventArgs e) => calls++;
    }

    [Test]
    public void Resolve_ReturnsNull_ForNullOrEmptyId()
    {
        Assert.That(ThemeRegistry.Resolve(null), Is.Null);
        Assert.That(ThemeRegistry.Resolve(""), Is.Null);
        Assert.That(ThemeRegistry.Resolve("   "), Is.Null);
    }
}
