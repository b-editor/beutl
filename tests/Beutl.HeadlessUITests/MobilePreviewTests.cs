using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.PreviewStreaming;
using Beutl.Testing.Headless;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
public sealed class MobilePreviewTests
{
    [Test]
    public async Task Pairing_requires_the_local_origin_and_an_unexpired_code_and_limits_attempts()
    {
        var clock = new FakeTimeProvider();
        await using var server = await MobilePreviewServer.StartAsync(IPAddress.Loopback, clock: clock);
        var cookies = new CookieContainer();
        using var http = CreateHttp(server, cookies);
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await http.GetStringAsync("/"), Does.Contain("src=\"viewer.js\""));
        Assert.That((await http.PostAsync("/edit", new StringContent(""))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        using var foreignHost = new HttpRequestMessage(HttpMethod.Get, "/");
        foreignHost.Headers.Host = "attacker.invalid";
        Assert.That((await http.SendAsync(foreignHost)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        foreach (string? origin in new[] { null, "http://attacker.invalid" })
        {
            using var anonymous = new HttpClient { BaseAddress = server.Endpoint };
            if (origin is not null) anonymous.DefaultRequestHeaders.Add("Origin", origin);
            Assert.That((await anonymous.PostAsync("/api/pair", new StringContent(server.PairingCode))).StatusCode,
                Is.EqualTo(HttpStatusCode.Forbidden));
        }
        Assert.That((await http.PostAsync("/api/pair", new StringContent("wrong!"))).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        using var paired = await http.PostAsync("/api/pair", new StringContent(server.PairingCode));
        Assert.That(paired.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        string cookie = paired.Headers.GetValues("Set-Cookie").Single();
        Assert.That(cookie, Does.Contain("httponly").IgnoreCase.And.Contain("samesite=strict").IgnoreCase);
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using var foreignSocket = new ClientWebSocket();
        foreignSocket.Options.Cookies = cookies;
        foreignSocket.Options.SetRequestHeader("Origin", "http://attacker.invalid");
        await Assert.ThrowsAsync<WebSocketException>(async () => await foreignSocket.ConnectAsync(SocketUri(server), CancellationToken.None));

        clock.Advance(TimeSpan.FromMinutes(11));
        using var another = CreateHttp(server, new CookieContainer());
        Assert.That((await another.PostAsync("/api/pair", new StringContent(server.PairingCode))).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "Code expiry must not interrupt an already paired viewer.");
        server.RenewPairingCode();
        for (int i = 0; i < 10; i++)
            Assert.That((await another.PostAsync("/api/pair", new StringContent("wrong!"))).StatusCode,
                Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await another.PostAsync("/api/pair", new StringContent(server.PairingCode))).StatusCode,
            Is.EqualTo(HttpStatusCode.TooManyRequests));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.That((await another.PostAsync("/api/pair", new StringContent(server.PairingCode))).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
        for (int i = 0; i < 2; i++)
        {
            using var device = CreateHttp(server, new CookieContainer());
            await Pair(device, server);
        }
        using var extra = CreateHttp(server, new CookieContainer());
        Assert.That((await extra.PostAsync("/api/pair", new StringContent(server.PairingCode))).StatusCode,
            Is.EqualTo(HttpStatusCode.Conflict));
        server.DisconnectAll();
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await Pair(extra, server);
    }

    [Test]
    public async Task Signaling_clients_keep_only_the_latest_bitmap_and_disconnect_releases_it()
    {
        await using var server = await MobilePreviewServer.StartAsync(IPAddress.Loopback);
        using var red = SolidFrame(255, 0, 0);
        server.Publish(red, UIToneMappingOperator.None, 0, false);
        Assert.That(red.RefCount, Is.EqualTo(1));
        var cookies = new CookieContainer();
        using var http = CreateHttp(server, cookies);
        await Pair(http, server);
        using var socket = await Connect(server, cookies);
        await WaitUntil(() => server.GetClients().Length == 1);
        string id = server.GetClients().Single().Id;
        int released = 0;
        for (int i = 0; i < 100; i++)
        {
            using var transient = Ref<Bitmap>.Create(new Bitmap(16, 16), () => Interlocked.Increment(ref released));
            server.Publish(transient, UIToneMappingOperator.None, 0, true);
        }
        using var blue = SolidFrame(0, 0, 255);
        server.Publish(blue, UIToneMappingOperator.None, 0, false);
        await WaitUntil(() => Volatile.Read(ref released) == 100);
        Assert.That(blue.RefCount, Is.GreaterThanOrEqualTo(2));
        server.Disconnect(id);
        await WaitUntil(() => server.GetClients().Length == 0 && blue.RefCount == 1);
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public void Audio_selects_the_visible_time_and_playback_and_crosses_block_boundaries()
    {
        var blocks = new[] {
            new AudioFrameSnapshot(Enumerable.Repeat(0.25f, 48000 * 2).ToArray(), 48000, 2, MediaTime.Zero) { PlaybackId = 7 },
            new AudioFrameSnapshot(Enumerable.Repeat(0.5f, 48000 * 2).ToArray(), 48000, 2, MediaTime.FromSeconds(1)) { PlaybackId = 7 },
            new AudioFrameSnapshot(Enumerable.Repeat(-0.25f, 48000 * 2).ToArray(), 48000, 2, MediaTime.Zero) { PlaybackId = 8 }
        };
        var sample = MobilePreviewServer.SampleAudio(blocks, 7, 0.99)!;
        Assert.That(sample[0], Is.EqualTo(0.25f));
        Assert.That(sample[^1], Is.EqualTo(0.5f));
        Assert.That(MobilePreviewServer.SampleAudio(blocks, 8, 0)![0], Is.EqualTo(-0.25f));
        Assert.That(MobilePreviewServer.SampleAudio(blocks, 9, 0), Is.Null);
    }

    [Test]
    public void Hdr_conversion_preserves_highlights_above_reference_white()
    {
        using var bitmap = new Bitmap(32, 32, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        bitmap.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)1, (Half)1, (Half)1, (Half)1));
        ushort white = BinaryPrimitives.ReadUInt16LittleEndian(MobilePreviewServer.ConvertFrame(bitmap, true, UIToneMappingOperator.ACES, 0));
        bitmap.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)4, (Half)4, (Half)4, (Half)1));
        ushort highlight = BinaryPrimitives.ReadUInt16LittleEndian(MobilePreviewServer.ConvertFrame(bitmap, true, UIToneMappingOperator.ACES, 0));
        Assert.That(white / 65535d, Is.EqualTo(0.581).Within(0.01), "PQ reference white is 203 nits.");
        Assert.That(highlight / 65535d, Is.EqualTo(0.73).Within(0.02));
        Assert.That(highlight, Is.GreaterThan(white));
    }

    [TestCase(UIToneMappingOperator.None, 0f, 0.25f, 137)]
    [TestCase(UIToneMappingOperator.Reinhard, 0f, 0.25f, 124)]
    [TestCase(UIToneMappingOperator.Reinhard, 1f, 0.25f, 156)]
    [TestCase(UIToneMappingOperator.Reinhard, 0f, 4f, 231)]
    [TestCase(UIToneMappingOperator.ACES, 0f, 4f, 252)]
    [TestCase(UIToneMappingOperator.Hable, 0f, 4f, 220)]
    public void Linear_frames_are_tone_mapped_and_encoded_as_sdr(UIToneMappingOperator mode, float exposure, float intensity, int expected)
    {
        using var bitmap = new Bitmap(32, 32, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        bitmap.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)intensity, (Half)intensity, (Half)intensity, (Half)1));
        using var decoded = SdrBitmap(bitmap, mode, exposure);
        SKColor pixel = decoded.GetPixel(16, 16);
        Assert.That(pixel.Red, Is.EqualTo(expected).Within(3));
        Assert.That(pixel.Green, Is.EqualTo(expected).Within(3));
        Assert.That(pixel.Blue, Is.EqualTo(expected).Within(3));
    }

    [AvaloniaTest]
    public void Mobile_sdr_colors_match_the_editor_surface()
    {
        using var bitmap = Ref<Bitmap>.Create(new Bitmap(32, 32, BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb));
        bitmap.Value.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)4, (Half)4, (Half)4, (Half)1));
        var image = new Beutl.Controls.BitmapView { Source = bitmap, ToneMapping = UIToneMappingOperator.Reinhard };
        var window = new Window { Content = image, Width = 64, Height = 64 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            using var screen = window.CaptureRenderedFrame()!;
            using var preview = SdrBitmap(bitmap.Value, UIToneMappingOperator.Reinhard, 0);
            var expected = HeadlessTestHelpers.ReadPixel(screen, new Avalonia.PixelPoint(32, 32));
            SKColor actual = preview.GetPixel(16, 16);
            Assert.That((int)actual.Red, Is.EqualTo(expected.R).Within(3));
            Assert.That((int)actual.Green, Is.EqualTo(expected.G).Within(3));
            Assert.That((int)actual.Blue, Is.EqualTo(expected.B).Within(3));
        }
        finally { image.Source = null; window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task Presentation_and_mobile_outputs_have_independent_lifetimes(bool closePresentationFirst)
    {
        TestGpu.RequireAnyDevice();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-preview", 320, 240);
        var owner = new Window { Content = new PlayerView { DataContext = editor.Player }, Width = 640, Height = 480 };
        var presentation = new PresentationWindow(editor.Player);
        var mobile = new MobilePreviewWindow(editor.Player);
        try
        {
            await TestShell.AddElementAsync(editor, () => new RectShape
            {
                Width = { Value = 60 },
                Height = { Value = 50 },
                Fill = { Value = new SolidColorBrush(Colors.Red) }
            }, MediaTime.Zero, MediaTime.FromSeconds(10));
            editor.GetRequiredService<IEditorSelection>().Select(editor.Scene.Children.Last());
            owner.Show();
            presentation.ShowOnScreen(owner, null, false);
            mobile.Show(owner);
            await mobile.StartSharingAsync(IPAddress.Loopback);
            var server = mobile.Server!;
            var cookies = new CookieContainer();
            using var http = CreateHttp(server, cookies);
            await Pair(http, server);
            using var socket = await Connect(server, cookies);
            await HeadlessTestHelpers.WaitUntilAsync(() => server.GetClients().Length == 1);
            HeadlessTestHelpers.Settle();
            editor.Player.QueuePreviewRender();
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
            using var cleanFrame = editor.Player.GetCleanPreviewFrame().Image!;
            using var clean = SdrBitmap(cleanFrame.Value, UIToneMappingOperator.None, 0);
            Assert.That(clean.Pixels.Count(p => p.Red > 200 && p.Green < 60 && p.Blue < 60), Is.GreaterThan(0));
            Assert.That(clean.Pixels.Count(p => p.Red > 240 && p.Green > 240 && p.Blue > 240), Is.Zero);
            HeadlessCapture.SaveIfRequested(mobile, nameof(MobilePreviewTests), "desktop-sharing");

            if (closePresentationFirst)
            {
                presentation.Close();
                Assert.That(editor.Player.IsCleanPreviewEnabled, Is.True);
                await mobile.StopSharingAsync();
            }
            else
            {
                await mobile.StopSharingAsync();
                Assert.That(editor.Player.IsCleanPreviewEnabled, Is.True);
                presentation.Close();
            }
            Assert.That(editor.Player.IsCleanPreviewEnabled, Is.False);
            Assert.That(editor.Player.CleanPreviewImage.Value, Is.Null);

            await mobile.StartSharingAsync(IPAddress.Loopback);
            Assert.That(mobile.Server, Is.Not.SameAs(server));
            if (closePresentationFirst) TestShell.Editor.SelectedTabItem.Value = null;
            else owner.Close();
            await mobile.StopSharingAsync();
            Assert.That(mobile.IsVisible, Is.False);
            Assert.That(mobile.Server, Is.Null);
        }
        finally
        {
            mobile.Close();
            await mobile.StopSharingAsync();
            presentation.Close();
            owner.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Paired_controls_follow_the_player_and_seek_safely_during_playback()
    {
        TestGpu.RequireAnyDevice();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-controls", 320, 240);
        var owner = new Window();
        var mobile = new MobilePreviewWindow(editor.Player);
        try
        {
            await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(120));
            owner.Show();
            mobile.Show(owner);
            await mobile.StartSharingAsync(IPAddress.Loopback);
            var server = mobile.Server!;
            var cookies = new CookieContainer();
            using var http = CreateHttp(server, cookies);
            await Pair(http, server);
            using var socket = await Connect(server, cookies);
            var state = await ReadMessage(socket, m => m.GetProperty("type").GetString() == "playback" && m.GetProperty("enabled").GetBoolean());
            Assert.That(state.GetProperty("frameRate").GetDouble(), Is.EqualTo(editor.Player.GetFrameRate().ToDouble()));

            editor.Player.CurrentFrame.Value = MediaTime.FromSeconds(1);
            await ReadMessage(socket, m => m.GetProperty("type").GetString() == "playback" && m.GetProperty("position").GetDouble() == 1);
            int request = 0;
            async Task<bool> Control(string action, double position = 0)
            {
                int id = ++request;
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "control", requestId = id, action, position }),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                var result = await ReadMessage(socket, m => m.GetProperty("type").GetString() == "control-result" && m.GetProperty("requestId").GetInt32() == id);
                return result.GetProperty("ok").GetBoolean();
            }

            Assert.That(await Control("play"), Is.True);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.CurrentFrame.Value > MediaTime.FromSeconds(1.1), pump: false);
            Assert.That(await Control("seek", 2.5), Is.True);
            Assert.That(editor.Player.IsPlaying.Value, Is.False);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            MediaTime frame = MediaTime.FromFrames(1, editor.Player.GetFrameRate());
            Assert.That(await Control("next"), Is.True);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(MediaTime.FromSeconds(2.5) + frame));
            Assert.That(await Control("previous"), Is.True);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(await Control("play"), Is.True);
            Assert.That(await Control("pause"), Is.True);
            Assert.That(editor.Player.IsPlaying.Value, Is.False);

            editor.IsEnabled.Value = false;
            var before = editor.Player.CurrentFrame.Value;
            Assert.That(await Control("next"), Is.False);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(before));
            editor.IsEnabled.Value = true;
            MediaTime end = editor.Player.Duration.Value;
            Assert.That(await Control("seek", double.MaxValue), Is.True);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(end));
            Assert.That(await Control("next"), Is.True, "Stepping follows the desktop even beyond the scene end.");
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(end + frame));
            Assert.That(await Control("seek", 0), Is.True);
            Assert.That(await Control("previous"), Is.True);
            Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(-frame));
        }
        finally
        {
            editor.IsEnabled.Value = true;
            await editor.Player.Pause();
            mobile.Close();
            await mobile.StopSharingAsync();
            owner.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [Test]
    public async Task Disconnecting_a_viewer_cancels_its_queued_controls()
    {
        await using var server = await MobilePreviewServer.StartAsync(IPAddress.Loopback);
        var first = new CookieContainer();
        var second = new CookieContainer();
        using var firstHttp = CreateHttp(server, first);
        using var secondHttp = CreateHttp(server, second);
        await Pair(firstHttp, server);
        await Pair(secondHttp, server);
        using var one = await Connect(server, first);
        await WaitUntil(() => server.GetClients().Length == 1);
        string firstId = server.GetClients().Single().Id;
        using var two = await Connect(server, second);
        await WaitUntil(() => server.GetClients().Length == 2);
        string secondId = server.GetClients().Single(c => c.Id != firstId).Id;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int applied = 0;
        server.ControlRequested = async (_, _, ct) =>
        {
            Interlocked.Increment(ref applied);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return true;
        };
        try
        {
            byte[] request = Encoding.UTF8.GetBytes("{\"type\":\"control\",\"requestId\":1,\"action\":\"next\"}");
            await one.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await two.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);
            server.Disconnect(secondId);
            release.TrySetResult();
            await ReadMessage(one, m => m.GetProperty("type").GetString() == "control-result");
            await server.DisposeAsync();
            Assert.That(applied, Is.EqualTo(1));
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaTest]
    public async Task Closing_during_startup_leaves_no_listener_or_preview_consumer()
    {
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-preview-cancel");
        var owner = new Window();
        var mobile = new MobilePreviewWindow(editor.Player);
        try
        {
            owner.Show();
            mobile.Show(owner);
            Task start = mobile.StartSharingAsync(IPAddress.Loopback);
            mobile.Close();
            try { await start; } catch (OperationCanceledException) { }
            await mobile.StopSharingAsync();
            Assert.That(mobile.Server, Is.Null);
            Assert.That(editor.Player.IsCleanPreviewEnabled, Is.False);
        }
        finally { owner.Close(); await TestReset.ResetShellAsync(); }
    }

    private static HttpClient CreateHttp(MobilePreviewServer server, CookieContainer cookies)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = server.Endpoint };
        client.DefaultRequestHeaders.Add("Origin", server.Endpoint.GetLeftPart(UriPartial.Authority));
        return client;
    }

    private static async Task Pair(HttpClient http, MobilePreviewServer server)
    {
        using var result = await http.PostAsync("/api/pair", new StringContent(server.PairingCode));
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    private static Uri SocketUri(MobilePreviewServer server) => new UriBuilder(server.Endpoint) { Scheme = "ws", Path = "/stream" }.Uri;

    private static async Task<ClientWebSocket> Connect(MobilePreviewServer server, CookieContainer cookies)
    {
        var socket = new ClientWebSocket();
        socket.Options.Cookies = cookies;
        socket.Options.SetRequestHeader("Origin", server.Endpoint.GetLeftPart(UriPartial.Authority));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(SocketUri(server), timeout.Token);
        return socket;
    }

    private static async Task<JsonElement> ReadMessage(ClientWebSocket socket, Func<JsonElement, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        byte[] buffer = new byte[65536];
        while (true)
        {
            int count = 0;
            ValueWebSocketReceiveResult message;
            do
            {
                message = await socket.ReceiveAsync(buffer.AsMemory(count), timeout.Token);
                Assert.That(message.MessageType, Is.EqualTo(WebSocketMessageType.Text));
                count += message.Count;
            } while (!message.EndOfMessage);
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            if (predicate(json.RootElement)) return json.RootElement.Clone();
        }
    }

    private static SKBitmap SdrBitmap(Bitmap image, UIToneMappingOperator mode, float exposure)
    {
        var (width, height) = MobilePreviewServer.FrameSize(image);
        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        byte[] pixels = MobilePreviewServer.ConvertFrame(image, false, mode, exposure);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, result.GetPixels(), pixels.Length);
        return result;
    }

    private static Ref<Bitmap> SolidFrame(byte red, byte green, byte blue)
    {
        var bitmap = new Bitmap(16, 16);
        bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(red, green, blue, 255));
        return Ref<Bitmap>.Create(bitmap);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
