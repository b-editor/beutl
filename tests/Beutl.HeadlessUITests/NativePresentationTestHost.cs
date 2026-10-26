using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Language;
using Beutl.Views;
using static Beutl.HeadlessUITests.MacNative;

namespace Beutl.HeadlessUITests;

internal static partial class NativeTimelineTestHost
{
    // Run the test assembly with --native-presentation-fullscreen. Headless only checks the
    // requested WindowState; AppKit can refuse that request, so check the native style mask too.
    private static async void RunPresentation()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        Window? owner = null;
        PlayerView? view = null;
        int exitCode = 1;
        try
        {
            var (_, _, editor) = await TestShell.OpenNewSceneAsync("native-presentation");
            view = new PlayerView { DataContext = editor.Player };
            owner = new Window { Content = view, Width = 640, Height = 480, Title = "Beutl presentation regression" };
            owner.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Button button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == Strings.PresentationWindow);
            Screen[] screens = owner.Screens.All.ToArray();
            for (int index = 0; index < screens.Length; index++)
            {
                // Exercise the monitor selection entry, including opening and dismissing its popup.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var popup = owner.OpenedPopups.Single(p => p.Child is MenuFlyoutPresenter);
                var menu = (MenuFlyoutPresenter)popup.Child!;
                menu.Items.OfType<MenuItem>().ElementAt(index + 1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                popup.IsOpen = false;
                PresentationWindow output = view.Presentation!;
                var handle = (IMacOSTopLevelPlatformHandle)output.TryGetPlatformHandle()!;
                bool IsNativeFullScreen() => ((long)Send(handle.NSWindow, Selector("styleMask")) & (1L << 14)) != 0;

                var elapsed = Stopwatch.StartNew();
                while (!IsNativeFullScreen() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(50);
                Console.WriteLine($"Monitor {index + 1}: requested={output.WindowState}, nativeFullscreen={IsNativeFullScreen()}, collectionBehavior={Send(handle.NSWindow, Selector("collectionBehavior"))}");
                Assert.That(IsNativeFullScreen(), Is.True, "AppKit must enter full screen, not only report the requested managed state.");
                await Task.Delay(750); // Let the Space transition complete before inspecting placement or leaving.
                Assert.That(output.Screens.ScreenFromWindow(output), Is.EqualTo(screens[index]));

                output.ToggleFullScreen();
                elapsed.Restart();
                while (IsNativeFullScreen() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(50);
                Assert.That(IsNativeFullScreen(), Is.False);
                await Task.Delay(750);
                output.Close();
                Assert.That(view.Presentation, Is.Null);
            }

            view.OpenPresentation(owner.Screens.Primary, false);
            owner.Close();
            Assert.That(view.Presentation, Is.Null, "Closing the editor must also close its independent output.");
            Console.WriteLine($"Native presentation: fullscreen entry, exit and placement passed on {screens.Length} monitor(s).");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            view?.Presentation?.Close();
            owner?.Close();
            lifetime.Shutdown(exitCode);
        }
    }
}
