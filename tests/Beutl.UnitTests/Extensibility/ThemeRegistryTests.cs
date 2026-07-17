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
    public void Unregister_StaleDescriptor_KeepsReplacement()
    {
        // The first owner unloads after a second one took over the id: removing purely by id would
        // evict the replacement and leave the theme picker without a theme that is still live.
        var first = new ThemeDescriptor("test.stale", "First", ThemeVariant.Dark);
        var second = new ThemeDescriptor("test.stale", "Second", ThemeVariant.Light);

        ThemeRegistry.Register(first);
        ThemeRegistry.Register(second);

        Assert.That(ThemeRegistry.Unregister(first), Is.False);
        Assert.That(ThemeRegistry.Resolve("test.stale"), Is.SameAs(second));

        Assert.That(ThemeRegistry.Unregister(second), Is.True);
        Assert.That(ThemeRegistry.Resolve("test.stale"), Is.Null);
    }

    [Test]
    public void Unregister_EqualValuedDescriptor_DoesNotEvictRegisteredInstance()
    {
        // ThemeDescriptor is a record, so an unrelated owner can hold a structurally equal value.
        // Only the instance that actually registered may remove the entry.
        var registered = new ThemeDescriptor("test.equal", "Same", ThemeVariant.Dark);
        var equalCopy = new ThemeDescriptor("test.equal", "Same", ThemeVariant.Dark);
        Assert.That(equalCopy, Is.EqualTo(registered));

        ThemeRegistry.Register(registered);

        Assert.That(ThemeRegistry.Unregister(equalCopy), Is.False);
        Assert.That(ThemeRegistry.Resolve("test.equal"), Is.SameAs(registered));
    }

    [Test]
    public void Unregister_StaleDescriptor_DoesNotRaiseChanged()
    {
        var first = new ThemeDescriptor("test.stalechanged", "First", ThemeVariant.Dark);
        var second = new ThemeDescriptor("test.stalechanged", "Second", ThemeVariant.Light);
        ThemeRegistry.Register(first);
        ThemeRegistry.Register(second);

        int calls = 0;
        ThemeRegistry.Changed += OnChanged;
        try
        {
            Assert.That(ThemeRegistry.Unregister(first), Is.False);
            Assert.That(calls, Is.EqualTo(0));
        }
        finally
        {
            ThemeRegistry.Changed -= OnChanged;
        }

        void OnChanged(object? sender, EventArgs e) => calls++;
    }

    [Test]
    public void Register_RejectsExtensionTakingOverABuiltinId()
    {
        // Letting an extension win a built-in id means its Unload evicts the built-in: the identity
        // check in Unregister cannot help, because by then the extension's descriptor is the entry.
        var builtin = new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark);
        ThemeRegistry.Register(builtin);

        var hijacker = new TestThemeExtension(BuiltinThemeIds.Dark, "Hijacked");

        Assert.Throws<ArgumentException>(() => hijacker.Load());
        Assert.Multiple(() =>
        {
            Assert.That(ThemeRegistry.Resolve(BuiltinThemeIds.Dark), Is.SameAs(builtin));
            Assert.That(ThemeRegistry.GetExtension(BuiltinThemeIds.Dark), Is.Null);
            Assert.That(hijacker.Descriptor, Is.Null, "a rejected Load must not leave a descriptor behind");
        });
    }

    [Test]
    public void Register_RejectsBuiltinId_EvenBeforeTheHostSeedsIt()
    {
        // Extensions load on background threads and can reach Register before ThemeService.Start.
        // An order-dependent rule would accept the id here and let the host silently overwrite it.
        Assert.That(ThemeRegistry.Resolve(BuiltinThemeIds.Dark), Is.Null, "precondition: nothing seeded");

        var hijacker = new TestThemeExtension(BuiltinThemeIds.Dark, "Early");

        Assert.Throws<ArgumentException>(() => hijacker.Load());
    }

    // Settings normalization rewrites these to the built-in on load, so accepting one would mean the
    // user's selection silently reverts to the built-in after a restart.
    [TestCase("Dark")]
    [TestCase("LIGHT")]
    [TestCase("HighContrast")]
    [TestCase("  system  ")]
    [TestCase("2")]
    [TestCase("0")]
    public void Register_RejectsExtensionUsingALegacyAliasOfABuiltinId(string id)
    {
        var ext = new TestThemeExtension(id, "Alias");

        Assert.Throws<ArgumentException>(() => ext.Load());
    }

    [TestCase("plugin.solarized")]
    [TestCase("2026")]
    [TestCase("darker")]
    [TestCase("my.dark")]
    public void Register_AcceptsExtensionIdsThatOnlyResembleBuiltins(string id)
    {
        var ext = new TestThemeExtension(id, "Custom");

        Assert.DoesNotThrow(() => ext.Load());
        Assert.That(ThemeRegistry.GetExtension(id), Is.SameAs(ext));
    }

    [Test]
    public void Register_AllowsHostToReregisterItsOwnBuiltin()
    {
        // Two ThemeService instances (or a restart of one) re-seed the built-ins; host-over-host is
        // not a takeover.
        var first = new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark);
        var second = new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark);
        ThemeRegistry.Register(first);

        Assert.DoesNotThrow(() => ThemeRegistry.Register(second));
        Assert.That(ThemeRegistry.Resolve(BuiltinThemeIds.Dark), Is.SameAs(second));
    }

    [Test]
    public void Register_AllowsExtensionToReplaceAnotherExtensionsId()
    {
        // Only the host is protected: extension-over-extension stays allowed and is handled by the
        // identity check in Unregister.
        var first = new TestThemeExtension("test.shared", "First");
        var second = new TestThemeExtension("test.shared", "Second");
        first.Load();

        Assert.DoesNotThrow(() => second.Load());
        Assert.That(ThemeRegistry.GetExtension("test.shared"), Is.SameAs(second));
    }

    [Test]
    public void GetOwner_ReturnsNull_ForAReplacedDescriptor()
    {
        // A holder of an already-replaced descriptor must not be handed the replacement's owner —
        // that would route OnApplied/OnReverted to an extension that never supplied it.
        var firstExt = new TestThemeExtension("test.owner", "First");
        firstExt.Load();
        ThemeDescriptor firstDescriptor = firstExt.Descriptor!;

        var secondExt = new TestThemeExtension("test.owner", "Second");
        secondExt.Load();

        Assert.That(ThemeRegistry.GetOwner(firstDescriptor), Is.Null);
        Assert.That(ThemeRegistry.GetOwner(secondExt.Descriptor!), Is.SameAs(secondExt));

        // GetExtension is id-based and cannot make this distinction; that is why GetOwner exists.
        Assert.That(ThemeRegistry.GetExtension("test.owner"), Is.SameAs(secondExt));
    }

    [Test]
    public void GetOwner_ReturnsNull_ForBuiltinAndUnregistered()
    {
        var builtin = new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark);
        ThemeRegistry.Register(builtin);

        Assert.That(ThemeRegistry.GetOwner(builtin), Is.Null);
        Assert.That(
            ThemeRegistry.GetOwner(new ThemeDescriptor("test.absent", "Absent", ThemeVariant.Dark)), Is.Null);
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

        ThemeNotifier.NotifyApplied(descriptor, ext);
        Assert.That(ext.AppliedCount, Is.EqualTo(1));
        Assert.That(ext.LastApplied?.Id, Is.EqualTo("test.notify"));

        ThemeNotifier.NotifyReverted(descriptor, ext);
        Assert.That(ext.RevertedCount, Is.EqualTo(1));

        // Built-in (no owning extension) must not throw.
        ThemeNotifier.NotifyApplied(new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark), null);
        ThemeNotifier.NotifyReverted(new ThemeDescriptor(BuiltinThemeIds.Dark, "Dark", ThemeVariant.Dark), null);

        ext.Unload();
    }

    [Test]
    public void NotifyReverted_UsesCapturedOwner_AfterUnregister()
    {
        // ThemeService captures the owner at apply time, then reverts once the extension has already
        // removed the theme from the registry. An id-based lookup would find nothing here, so
        // OnReverted would never fire and the extension could not release its apply-time resources.
        var ext = new TestThemeExtension("test.revert", "Revert");
        ext.Load();
        ThemeDescriptor descriptor = ThemeRegistry.Resolve("test.revert")!;

        ext.Unload();
        Assert.That(ThemeRegistry.GetExtension("test.revert"), Is.Null);

        ThemeNotifier.NotifyReverted(descriptor, ext);

        Assert.That(ext.RevertedCount, Is.EqualTo(1));
    }

    [Test]
    public void NotifyApplied_SwallowsExtensionException()
    {
        var ext = new ThrowingThemeExtension("test.throw", "Throw");
        ext.Load();
        ThemeDescriptor descriptor = ThemeRegistry.Resolve("test.throw")!;

        Assert.DoesNotThrow(() => ThemeNotifier.NotifyApplied(descriptor, ext));
        Assert.DoesNotThrow(() => ThemeNotifier.NotifyReverted(descriptor, ext));

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
