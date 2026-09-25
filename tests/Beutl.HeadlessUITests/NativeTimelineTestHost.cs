using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// AppKit must run on the process main thread, independently of the headless test dispatcher.
internal static partial class NativeTimelineTestHost
{
    private static ulong s_eventTimestamp = 12_345_678_901;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS() || args is not ["--native-timeline-scroll"])
            return 64;

        BeutlHomeIsolation.Begin("beutl-native-scroll");
        OpenALPreload.EnsureLoaded();
        try
        {
            return AppBuilder.Configure<TestApp>()
                .UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(Run, DispatcherPriority.Background))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            BeutlHomeIsolation.End();
        }
    }

    private static async void Run()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        Window? window = null;
        TimelineTabView? view = null;
        int exitCode = 1;
        try
        {
            await TestReset.ResetShellAsync();
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "project");
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "native-scroll", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            scene.Duration = TimeSpan.FromSeconds(30);
            TestShell.Editor.ActivateTabItem(scene);
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            TimelineTabViewModel model = editor.FindToolTab<TimelineTabViewModel>()!;
            view = new TimelineTabView { DataContext = model }; // Use the production detector.
            window = new Window
            {
                Content = view,
                Width = 960,
                Height = 420,
                Title = "Beutl native scroll regression",
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            ScrollViewer content = view.FindControl<ScrollViewer>("ContentScroll")!;
            Border ruler = view.FindControl<Border>("RulerBar")!;
            int events = 0;
            bool expectedGesture = false;
            view.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                Assert.That(NativeScrollInput.UsesGestureAxes(e), Is.EqualTo(expectedGesture));
                events++;
            }, RoutingStrategies.Tunnel);

            foreach (bool swap in new[] { false, true })
                foreach (Control target in new Control[] { content, ruler })
                    foreach (var sample in new[]
                    {
                (X: -10, Y: 0, Phase: 1L, Momentum: 0L, Gesture: true),
                (X: 0, Y: -10, Phase: 2L, Momentum: 0L, Gesture: true),
                (X: 0, Y: 0, Phase: 4L, Momentum: 0L, Gesture: true),
                (X: -10, Y: -5, Phase: 0L, Momentum: 1L, Gesture: true),
                (X: -10, Y: -5, Phase: 0L, Momentum: 2L, Gesture: true),
                (X: 0, Y: 0, Phase: 0L, Momentum: 3L, Gesture: true),
                (X: 0, Y: -10, Phase: 0L, Momentum: 0L, Gesture: false)
            })
                    {
                        GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection = swap;
                        model.Options.Value = model.Options.Value with { Scale = 1, Offset = new System.Numerics.Vector2(300, 200) };
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        Assert.That(content.Offset, Is.EqualTo(new Vector(300, 200)));
                        expectedGesture = sample.Gesture;
                        int before = events;
                        Point position = target.TranslatePoint(new Point(100, ReferenceEquals(target, ruler) ? 4 : 80), window)!.Value;
                        DispatchScroll(window, position, sample.X, sample.Y, sample.Phase, sample.Momentum);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        int expectedEvents = sample.X == 0 && sample.Y == 0 ? 0 : 1;
                        Assert.That(events, Is.EqualTo(before + expectedEvents), "AppKit must deliver nonzero wheel deltas exactly once.");
                        bool keepAxes = sample.Gesture || swap;
                        Assert.That(content.Offset.X, Is.EqualTo(300 - (keepAxes ? sample.X : sample.Y)).Within(0.001));
                        Assert.That(content.Offset.Y, Is.EqualTo(200 - (keepAxes ? sample.Y : sample.X)).Within(0.001));
                    }
            Console.WriteLine($"Native timeline scroll: {events} AppKit events passed.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            if (view != null) view.DataContext = null;
            window?.Close();
            lifetime.Shutdown(exitCode);
        }
    }

    private static void DispatchScroll(Window window, Point position, int x, int y, long phase, long momentum)
    {
        var handle = (IMacOSTopLevelPlatformHandle)window.TryGetPlatformHandle()!;
        nint app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        nint cgEvent = CGEventCreateScrollWheelEvent2(0, 0, 2, y, x, 0);
        try
        {
            double screenHeight = CGDisplayBounds(CGMainDisplayID()).Height;
            // A process-local CGEvent has no WindowServer-assigned target window.
            // Give it window-local Cocoa coordinates and dispatch to Avalonia's real NSView below.
            CGEventSetLocation(cgEvent, new NativePoint(position.X, screenHeight - window.ClientSize.Height + position.Y));
            CGEventSetFlags(cgEvent, 0);
            // Include fractional milliseconds to exercise Avalonia.Native's timestamp conversion.
            CGEventSetTimestamp(cgEvent, s_eventTimestamp += 1_234_567);
            CGEventSetIntegerValueField(cgEvent, 99, phase);
            CGEventSetIntegerValueField(cgEvent, 123, momentum);
            nint nativeEvent = Send(GetClass("NSEvent"), Selector("eventWithCGEvent:"), cgEvent);
            Assert.That(nativeEvent, Is.Not.EqualTo(nint.Zero));
            PostEvent(app, Selector("postEvent:atStart:"), nativeEvent, 1);
            nint nextEvent = NextEvent(app, Selector("nextEventMatchingMask:untilDate:inMode:dequeue:"),
                1UL << 22, Send(GetClass("NSDate"), Selector("distantPast")), CreateString("kCFRunLoopDefaultMode"), 1);
            Assert.That(nextEvent, Is.Not.EqualTo(nint.Zero));
            Assert.That(Send(app, Selector("currentEvent")), Is.EqualTo(nextEvent));
            // NSApplication establishes currentEvent via its queue. Deliver to the real
            // AppKit responder without global input injection or replacing the detector.
            Send(handle.NSView, Selector("scrollWheel:"), nextEvent);
        }
        finally
        {
            CFRelease(cgEvent);
            Send(pool, Selector("drain"));
        }
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(double X, double Y);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(double X, double Y, double Width, double Height);

    private static nint CreateString(string value) => String(GetClass("NSString"), Selector("stringWithUTF8String:"), value);

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint String(nint receiver, nint selector, string value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void PostEvent(nint receiver, nint selector, nint nativeEvent, byte atStart);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint NextEvent(nint receiver, nint selector, ulong mask, nint date, nint mode, byte dequeue);
    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetLocation(nint cgEvent, NativePoint point);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(nint cgEvent, ulong flags);
    [LibraryImport(CoreGraphics)]
    private static partial uint CGMainDisplayID();
    [LibraryImport(CoreGraphics)]
    private static partial NativeRect CGDisplayBounds(uint display);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetTimestamp(nint cgEvent, ulong timestamp);
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetIntegerValueField(nint cgEvent, uint field, long value);
    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint value);
}

[TestFixture]
public class NativeTimelineIntegrationTests
{
    [Test]
    [Platform("MacOSX")]
    public async Task AppKit_wheel_dispatch_uses_real_detector_and_updates_timeline()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeTimelineTestHost).Assembly.Location);
        start.ArgumentList.Add("--native-timeline-scroll");
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.That(process.ExitCode, Is.Zero, await output + "\n" + await error);
        Assert.That(await output, Does.Contain("20 AppKit events passed."));
    }
}
