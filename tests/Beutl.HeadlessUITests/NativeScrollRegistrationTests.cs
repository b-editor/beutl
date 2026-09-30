using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NativeScrollRegistrationTests
{
    [AvaloniaTest]
    public void Multiple_timelines_share_registration_and_track_other_windows_until_last_owner_detaches()
    {
        var first = new Window();
        var second = new Window();
        var existing = new Window();
        var later = new Window();
        var afterRelease = new Window();
        var native = new RecordingNativeInput();
        var manager = new NativeScrollInput.Windows.RegistrationManager(native.Register, native.AttachHook, () => [existing, first]);
        IDisposable? firstOwner = null;
        IDisposable? duplicateOwner = null;
        IDisposable? secondOwner = null;
        try
        {
            existing.Show();
            firstOwner = manager.Attach(first) ?? throw new AssertionException("Initial registration failed.");
            Assert.Multiple(() =>
            {
                Assert.That(native.RegistrationCount, Is.EqualTo(1));
                Assert.That(native.HookedRoots, Is.EquivalentTo(new[] { first, existing }));
                Assert.That(native.HooksAtRegistration, Is.EqualTo(2), "Protect existing windows before enabling pointer delivery.");
            });

            duplicateOwner = manager.Attach(first) ?? throw new AssertionException("The shared registration was not reused.");
            secondOwner = manager.Attach(second) ?? throw new AssertionException("The second timeline could not attach.");
            first.Show();
            second.Show();
            later.Show();
            Assert.Multiple(() =>
            {
                Assert.That(native.RegistrationCount, Is.EqualTo(1));
                Assert.That(native.HookedRoots, Is.EquivalentTo(new[] { first, second, existing, later }));
            });

            later.Hide();
            later.Show();
            later.Close();
            Assert.That(native.HookedRoots, Does.Not.Contain(later));
            firstOwner.Dispose();
            firstOwner.Dispose();
            duplicateOwner.Dispose();
            Assert.That(native.RegistrationCount, Is.EqualTo(1), "The second timeline still owns the registration.");
            secondOwner.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(native.RegistrationCount, Is.Zero);
                Assert.That(native.HookedRoots, Is.Empty);
            });
            afterRelease.Show();
            Assert.That(native.HookedRoots, Is.Empty, "The visibility subscription must be removed with the final owner.");

            using (manager.Attach(second))
            {
                Assert.That(native.RegistrationCount, Is.EqualTo(1), "A later attachment must create a new registration.");
            }
            Assert.That(native.RegistrationCount, Is.Zero);
        }
        finally
        {
            firstOwner?.Dispose();
            duplicateOwner?.Dispose();
            secondOwner?.Dispose();
            foreach (Window window in new[] { first, second, existing, later, afterRelease }) window.Close();
        }
    }

    [AvaloniaTest]
    public void Failed_registration_removes_hooks_and_can_retry_without_observing_future_windows()
    {
        var first = new Window();
        var later = new Window();
        var native = new RecordingNativeInput { RegistrationSucceeds = false };
        var manager = new NativeScrollInput.Windows.RegistrationManager(native.Register, native.AttachHook, () => []);
        try
        {
            Assert.That(manager.Attach(first), Is.Null);
            later.Show();
            Assert.Multiple(() =>
            {
                Assert.That(native.HookedRoots, Is.Empty);
                Assert.That(native.RegistrationCount, Is.Zero);
                Assert.That(native.UnregisterCalls, Is.Zero);
            });

            native.RegistrationSucceeds = true;
            using (manager.Attach(first))
                Assert.That(native.RegistrationCount, Is.EqualTo(1));
            Assert.That(native.UnregisterCalls, Is.EqualTo(1));
        }
        finally
        {
            first.Close();
            later.Close();
        }
    }

    [AvaloniaTest]
    public void Unsupported_roots_are_not_hooked_and_closing_a_window_releases_its_hook()
    {
        var first = new Window();
        var unsupported = new Window { Tag = "unsupported" };
        var native = new RecordingNativeInput();
        var manager = new NativeScrollInput.Windows.RegistrationManager(native.Register, native.AttachHook, () => [unsupported]);
        try
        {
            using (manager.Attach(first))
            {
                first.Show();
                unsupported.Show();
                Assert.That(native.HookedRoots, Is.EquivalentTo(new[] { first }));
                first.Close();
                Assert.That(native.HookedRoots, Is.Empty);
                Assert.That(native.RegistrationCount, Is.EqualTo(1), "A hook closing does not dispose another timeline's lease.");
            }
            Assert.That(native.RegistrationCount, Is.Zero);
        }
        finally
        {
            first.Close();
            unsupported.Close();
        }
    }

    [AvaloniaTest]
    public void Missing_native_api_does_not_install_any_hooks()
    {
        var window = new Window();
        var manager = new NativeScrollInput.Windows.RegistrationManager(null,
            _ => throw new AssertionException("Unsupported platforms must not install hooks."), () => []);
        try { Assert.That(manager.Attach(window), Is.Null); }
        finally { window.Close(); }
    }

    private sealed class RecordingNativeInput
    {
        public HashSet<TopLevel> HookedRoots { get; } = [];
        public bool RegistrationSucceeds { get; set; } = true;
        public int RegistrationCount { get; private set; }
        public int UnregisterCalls { get; private set; }
        public int HooksAtRegistration { get; private set; }

        public bool Register(bool enable)
        {
            if (enable)
            {
                if (!RegistrationSucceeds) return false;
                HooksAtRegistration = HookedRoots.Count;
                RegistrationCount++;
            }
            else
            {
                Assert.That(RegistrationCount, Is.GreaterThan(0));
                RegistrationCount--;
                UnregisterCalls++;
            }
            return true;
        }

        public IDisposable? AttachHook(TopLevel root)
        {
            if (Equals(root.Tag, "unsupported")) return null;
            Assert.That(HookedRoots.Add(root), Is.True, "A window must not be hooked twice.");
            return System.Reactive.Disposables.Disposable.Create(() =>
                Assert.That(HookedRoots.Remove(root), Is.True, "Every installed hook must be removed exactly once."));
        }
    }
}
