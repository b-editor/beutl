using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using BtlMatrix = Beutl.Graphics.Matrix;

namespace Beutl.HeadlessUITests;

internal static partial class NativeTimelineTestHost
{
    private const uint WmMouseWheel = 0x020A;
    private const int MkControl = 0x0008;

    private static int RunWindowsPinch(string[] args)
    {
        BeutlHomeIsolation.Begin("beutl-native-windows-pinch");
        OpenALPreload.EnsureLoaded();
        try
        {
            return AppBuilder.Configure<TestApp>()
                .UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(RunWindowsPinchAsync, DispatcherPriority.Background))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            BeutlHomeIsolation.End();
        }
    }

    private static async void RunWindowsPinchAsync()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        int exitCode = 1;
        try
        {
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("native-win-pinch", duration: MediaTime.FromSeconds(30));

            await VerifyTimelinePinchAsync(editor);
            await VerifyNodeGraphPinchAsync(editor);
            await VerifyPreviewPinchAsync(editor);

            Console.WriteLine("Native Windows pinch: timeline, node graph, and preview passed.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            lifetime.Shutdown(exitCode);
        }
    }

    private static async Task VerifyTimelinePinchAsync(EditViewModel editor)
    {
        TimelineTabViewModel model = editor.FindToolTab<TimelineTabViewModel>()!;
        var view = new TimelineTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 960,
            Height = 420,
            Title = "Beutl native Windows timeline pinch",
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await FlushAsync();

            nint hwnd = RequireHwnd(window);
            ScrollViewer content = view.FindControl<ScrollViewer>("ContentScroll")!;
            Border ruler = view.FindControl<Border>("RulerBar")!;
            int observedWheelEvents = 0;
            view.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                // Verify Avalonia itself delivered None (physical Ctrl is up), exercising the MK_CONTROL path.
                Assert.That(e.KeyModifiers, Is.EqualTo(KeyModifiers.None));
                observedWheelEvents++;
            }, RoutingStrategies.Tunnel);

            foreach (Control target in new Control[] { content, ruler })
            {
                model.Options.Value = model.Options.Value with { Scale = 1, Offset = new System.Numerics.Vector2(300, 200) };
                await FlushAsync();
                Assert.That(content.Offset, Is.EqualTo(new Vector(300, 200)));

                Point point = target.TranslatePoint(new Point(100, ReferenceEquals(target, ruler) ? 4 : 80), window)!.Value;
                SendWheel(hwnd, window, point, delta: 60, mkControl: true);
                await FlushAsync();

                float expectedScale = MathF.Pow(1.2f, 0.5f);
                Assert.Multiple(() =>
                {
                    Assert.That(model.Options.Value.Scale, Is.EqualTo(expectedScale).Within(0.001f));
                    Assert.That((content.Offset.X + 100) / model.Options.Value.Scale, Is.EqualTo(400).Within(1.5));
                    Assert.That(content.Offset.Y, Is.EqualTo(200).Within(0.001));
                });

                SendWheel(hwnd, window, point, delta: -60, mkControl: true);
                await FlushAsync();
                Assert.That(model.Options.Value.Scale, Is.EqualTo(1f).Within(0.001f));

                Vector beforePlainScroll = content.Offset;
                SendWheel(hwnd, window, point, delta: -120, mkControl: false);
                await FlushAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(model.Options.Value.Scale, Is.EqualTo(1f).Within(0.001f));
                    Assert.That(content.Offset, Is.Not.EqualTo(beforePlainScroll), "Plain wheel must scroll instead of zooming.");
                });
            }

            Assert.That(observedWheelEvents, Is.EqualTo(6));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    private static async Task VerifyNodeGraphPinchAsync(EditViewModel editor)
    {
        var expression = new ExpressionNode { Position = { Value = (24, 24) } };
        expression.Expression.Property!.SetValue("1");
        var output = new OutputNode { Position = { Value = (320, 96) } };
        var graph = new GraphModel();
        graph.Nodes.AddRange([expression, output]);
        graph.Connect(output.InputPort, expression.Output);
        using var model = new NodeGraphViewModel(graph, editor);
        // Simulate a touchpad scroll source so plain wheel pans while MK_CONTROL wheel zooms.
        var view = new NodeGraphView(_ => true) { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 640,
            Height = 360,
            Title = "Beutl native Windows node graph pinch",
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await FlushAsync();

            nint hwnd = RequireHwnd(window);
            ZoomBorder zoom = view.FindControl<ZoomBorder>("zoomBorder")!;
            Canvas canvas = view.FindControl<Canvas>("canvas")!;
            zoom.SetMatrix(new Matrix(1.5, 0, 0, 1.5, 45, 30), true);
            zoom.EnableAnimations = true;
            await FlushAsync();

            Point point = new(window.ClientSize.Width - 60, window.ClientSize.Height - 60);
            Point anchor = window.TranslatePoint(point, canvas)!.Value;

            SendWheel(hwnd, window, point, delta: 60, mkControl: true);
            await FlushAsync();
            Assert.That(zoom.ZoomX, Is.EqualTo(1.5 * Math.Pow(1.2, 0.5)).Within(0.001));
            Point afterZoom = canvas.TranslatePoint(anchor, window)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(afterZoom.X, Is.EqualTo(point.X).Within(1.5));
                Assert.That(afterZoom.Y, Is.EqualTo(point.Y).Within(1.5));
            });

            SendWheel(hwnd, window, point, delta: -60, mkControl: true);
            await FlushAsync();
            Assert.That(zoom.ZoomX, Is.EqualTo(1.5).Within(0.001));

            double offsetYBeforePan = zoom.OffsetY;
            SendWheel(hwnd, window, point, delta: -60, mkControl: false);
            await FlushAsync();
            Assert.Multiple(() =>
            {
                Assert.That(zoom.ZoomX, Is.EqualTo(1.5).Within(0.001));
                Assert.That(zoom.OffsetY, Is.EqualTo(offsetYBeforePan - 25).Within(0.001));
            });
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    private static async Task VerifyPreviewPinchAsync(EditViewModel editor)
    {
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window
        {
            Content = view,
            Width = 800,
            Height = 600,
            Title = "Beutl native Windows preview pinch",
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await FlushAsync();

            nint hwnd = RequireHwnd(window);
            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            player.IsMoveMode.Value = true;
            player.IsCropMode.Value = player.IsCameraMode.Value = player.IsHandMode.Value = false;
            player.FrameMatrix.Value = BtlMatrix.Identity;
            await FlushAsync();

            Point point = framePanel.TranslatePoint(
                new Point(framePanel.Bounds.Width / 2, framePanel.Bounds.Height / 2), window)!.Value;

            SendWheel(hwnd, window, point, delta: 60, mkControl: true);
            await FlushAsync();
            Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(MathF.Pow(1.2f, 0.5f)).Within(0.001f));

            SendWheel(hwnd, window, point, delta: -60, mkControl: true);
            await FlushAsync();
            Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(1f).Within(0.001f));

            player.FrameMatrix.Value = BtlMatrix.Identity;
            await FlushAsync();
            SendWheel(hwnd, window, point, delta: 120, mkControl: false);
            await FlushAsync();
            Assert.That(player.FrameMatrix.Value, Is.EqualTo(BtlMatrix.Identity), "Plain wheel on fitted preview must not zoom.");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    private static nint RequireHwnd(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        Assert.That(handle, Is.Not.Null);
        Assert.That(handle!.HandleDescriptor, Is.EqualTo("HWND"));
        return handle.Handle;
    }

    private static void SendWheel(nint hwnd, Window window, Point clientPoint, short delta, bool mkControl)
    {
        PixelPoint screen = window.PointToScreen(clientPoint);
        nint wParam = unchecked((nint)(((int)delta << 16) | (mkControl ? MkControl : 0)));
        nint lParam = unchecked((nint)((screen.Y << 16) | (screen.X & 0xFFFF)));
        SendMessage(hwnd, WmMouseWheel, wParam, lParam);
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}

[TestFixture]
public class NativeWindowsPinchIntegrationTests
{
    [Test]
    [Platform("Win")]
    public async Task Win32_mk_control_wheel_zooms_timeline_node_graph_and_preview()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(NativeTimelineTestHost).Assembly.Location);
        start.ArgumentList.Add("--native-windows-pinch");
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
        Assert.That(await output, Does.Contain("Native Windows pinch: timeline, node graph, and preview passed."));
    }
}
