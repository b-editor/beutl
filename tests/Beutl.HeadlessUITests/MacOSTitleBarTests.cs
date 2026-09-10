using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Helpers;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class MacOSTitleBarTests
{
    [AvaloniaTest]
    public void Fullscreen_exit_restores_the_toolbar()
    {
        var window = new Window();
        var native = new RecordingToolbar();
        using var titleBar = new MacOSTitleBar(window, native);

        try
        {
            window.WindowState = WindowState.FullScreen;
            HeadlessTestHelpers.Settle();
            window.WindowState = WindowState.Normal;
            HeadlessTestHelpers.Settle();

            Assert.That(native.FullscreenUpdates, Is.EqualTo(new[] { false, true, false }));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Initially_fullscreen_window_does_not_attach_the_toolbar()
    {
        var window = new Window { WindowState = WindowState.FullScreen };
        var native = new RecordingToolbar();
        using var titleBar = new MacOSTitleBar(window, native);

        try
        {
            Assert.That(native.FullscreenUpdates, Is.EqualTo(new[] { true }));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Queued_update_uses_the_latest_window_state()
    {
        var window = new Window();
        var native = new RecordingToolbar();
        using var titleBar = new MacOSTitleBar(window, native);

        try
        {
            window.WindowState = WindowState.FullScreen;
            window.WindowState = WindowState.Normal;
            HeadlessTestHelpers.Settle();

            Assert.That(native.FullscreenUpdates, Is.EqualTo(new[] { false, false }));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Closing_with_a_queued_update_releases_once_without_accessing_the_closed_window()
    {
        var window = new Window();
        var native = new RecordingToolbar();
        using var titleBar = new MacOSTitleBar(window, native);
        window.Show();

        window.WindowState = WindowState.FullScreen;
        window.Close();
        titleBar.Dispose();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(native.DisposeCount, Is.EqualTo(1));
            Assert.That(native.FullscreenUpdates, Is.EqualTo(new[] { false }));
        });
    }

    [AvaloniaTest]
    public void Cancelled_close_keeps_the_toolbar_active()
    {
        var window = new Window();
        var native = new RecordingToolbar();
        using var titleBar = new MacOSTitleBar(window, native);
        window.Show();
        window.Closing += CancelClose;

        try
        {
            window.Close();
            window.WindowState = WindowState.FullScreen;
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(native.DisposeCount, Is.Zero);
                Assert.That(native.FullscreenUpdates, Is.EqualTo(new[] { false, true }));
            });
        }
        finally
        {
            window.Closing -= CancelClose;
            window.Close();
        }

        static void CancelClose(object? sender, WindowClosingEventArgs e) => e.Cancel = true;
    }

    [AvaloniaTest]
    public void Headless_window_does_not_load_AppKit()
    {
        var window = new Window { ExtendClientAreaToDecorationsHint = true };

        try
        {
            Assert.That(MacOSTitleBar.TryAttach(window), Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class RecordingToolbar : MacOSTitleBar.INativeToolbar
    {
        public List<bool> FullscreenUpdates { get; } = [];

        public int DisposeCount { get; private set; }

        public void Update(bool fullScreen)
        {
            Assert.That(DisposeCount, Is.Zero, "A closed NSWindow must not receive another native update.");
            FullscreenUpdates.Add(fullScreen);
        }

        public void Dispose() => DisposeCount++;
    }
}
