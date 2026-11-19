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
    public void Failed_thread_registration_keeps_hooks_without_unregistering_and_cleans_up_on_dispose()
    {
        var first = new Window();
        var later = new Window();
        var afterRelease = new Window();
        var native = new RecordingNativeInput { RegistrationSucceeds = false };
        var manager = new NativeScrollInput.Windows.RegistrationManager(native.Register, native.AttachHook, () => []);
        try
        {
            using (manager.Attach(first))
            {
                later.Show();
                Assert.Multiple(() =>
                {
                    Assert.That(native.HookedRoots, Is.EquivalentTo(new[] { first, later }));
                    Assert.That(native.RegistrationCount, Is.Zero);
                    Assert.That(native.UnregisterCalls, Is.Zero);
                });
            }

            afterRelease.Show();
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
            afterRelease.Close();
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
    public void Missing_touchpad_thread_api_still_hooks_windows_until_last_owner_detaches()
    {
        var window = new Window();
        var later = new Window();
        var afterRelease = new Window();
        var native = new RecordingNativeInput();
        int resets = 0;
        var manager = new NativeScrollInput.Windows.RegistrationManager(null, native.AttachHook, () => [], () => resets++);
        try
        {
            using (manager.Attach(window))
            {
                later.Show();
                Assert.That(native.HookedRoots, Is.EquivalentTo(new[] { window, later }));
            }

            afterRelease.Show();
            Assert.Multiple(() =>
            {
                Assert.That(native.HookedRoots, Is.Empty);
                Assert.That(resets, Is.EqualTo(1));
            });
        }
        finally
        {
            window.Close();
            later.Close();
            afterRelease.Close();
        }
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
