using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// #2752: an exception that escapes an async void handler is rethrown on the UI thread, which ended the process.
[TestFixture, NonParallelizable]
public sealed class UiExceptionBoundaryTests
{
    [AvaloniaTest]
    public void Exception_from_an_async_void_click_handler_is_reported_instead_of_ending_the_process()
    {
        using var notifications = new NotificationCapture();
        using IDisposable boundary = UiExceptionBoundary.Install(Dispatcher.UIThread);
        var button = new Button { Content = "Throw", Width = 120, Height = 32 };
        button.Click += ThrowAfterAwait;
        var window = new Window { Content = button, Width = 240, Height = 120 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();

            // Without the boundary, the rethrown exception escapes the dispatcher loop here.
            HeadlessTestHelpers.Click(button);

            Notification notification = notifications.Single();
            Assert.Multiple(() =>
            {
                Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
                Assert.That(notification.Message, Is.EqualTo("Thrown by a click handler."));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Fatal_exception_is_left_for_the_crash_handler()
    {
        using var notifications = new NotificationCapture();
        using IDisposable boundary = UiExceptionBoundary.Install(Dispatcher.UIThread);

        Dispatcher.UIThread.Post(static () => throw new OutOfMemoryException());

        Assert.Throws<OutOfMemoryException>(() => Dispatcher.UIThread.RunJobs());
        Assert.That(notifications.All, Is.Empty);
    }

    private static async void ThrowAfterAwait(object? sender, RoutedEventArgs e)
    {
        await Task.Yield();
        throw new InvalidOperationException("Thrown by a click handler.");
    }
}
