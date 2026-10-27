using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Editor.Models;
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
    public async Task A_slow_viewer_gets_the_latest_frame_and_replaced_bitmaps_are_released()
    {
        await using var server = await MobilePreviewServer.StartAsync(IPAddress.Loopback);
        using var red = SolidFrame(255, 0, 0);
        server.Publish(red, UIToneMappingOperator.None, 0, false);
        Assert.That(red.RefCount, Is.EqualTo(1), "A server with no viewers must not retain or encode frames.");
        var cookies = new CookieContainer();
        using var http = CreateHttp(server, cookies);
        await Pair(http, server);
        using var socket = await Connect(server, cookies);
        await WaitUntil(() => server.GetClients().Length == 1);
        string id = server.GetClients().Single().Id;
        server.Publish(red, UIToneMappingOperator.None, 0, false);
        using var first = SKBitmap.Decode(await ReceiveImage(socket));
        Assert.That(first.GetPixel(8, 8).Red, Is.GreaterThan(245));

        var fastCookies = new CookieContainer();
        using var fastHttp = CreateHttp(server, fastCookies);
        await Pair(fastHttp, server);
        using var fastSocket = await Connect(server, fastCookies);
        await WaitUntil(() => server.GetClients().Length == 2);

        int released = 0;
        for (int i = 0; i < 100; i++)
        {
            using var transient = Ref<Bitmap>.Create(new Bitmap(16, 16), () => Interlocked.Increment(ref released));
            server.Publish(transient, UIToneMappingOperator.None, 0, true);
        }
        Assert.That(Volatile.Read(ref released), Is.GreaterThanOrEqualTo(98));
        using var blue = SolidFrame(0, 0, 255);
        server.Publish(blue, UIToneMappingOperator.None, 0, false);
        await WaitUntil(() => blue.RefCount == 1 && Volatile.Read(ref released) == 100);
        // This viewer can receive while the first viewer has not acknowledged its initial image.
        using var fastFrame = SKBitmap.Decode(await ReceiveImage(fastSocket));
        // It may have already sent one intermediate frame before the final blue image arrived.
        if (fastFrame.GetPixel(8, 8).Blue < 245)
        {
            await fastSocket.SendAsync("ready"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
            using var fastLatest = SKBitmap.Decode(await ReceiveImage(fastSocket));
            Assert.That(fastLatest.GetPixel(8, 8).Blue, Is.GreaterThan(245));
        }
        await socket.SendAsync("re"u8.ToArray().AsMemory(), WebSocketMessageType.Text, false, CancellationToken.None);
        await socket.SendAsync("ady"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
        using var latest = SKBitmap.Decode(await ReceiveImage(socket));
        Assert.That(latest.GetPixel(8, 8).Blue, Is.GreaterThan(245), "Queued intermediate images must be discarded.");
        server.Disconnect(id);
        await WaitUntil(() => server.GetClients().Length == 1);
        Assert.That((await http.GetAsync("/api/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Audio_is_opt_in_timestamped_and_does_not_block_video_or_pause()
    {
        await using var server = await MobilePreviewServer.StartAsync(IPAddress.Loopback);
        var cookies = new CookieContainer();
        using var http = CreateHttp(server, cookies);
        await Pair(http, server);
        using var socket = await Connect(server, cookies);
        await WaitUntil(() => server.GetClients().Length == 1);
        server.PublishAudio(new AudioFrameSnapshot([1, -1, 0.5f, -0.5f], 48000, 2, MediaTime.FromSeconds(2)) { PlaybackId = 7 });
        // Muted viewers receive the video first, even when audio is available.
        using var red = SolidFrame(255, 0, 0);
        server.Publish(red, UIToneMappingOperator.None, 0, true, 2.1, 7);
        byte[] video = await ReceivePacket(socket);
        Assert.That(video[0], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(video.AsSpan(8)), Is.EqualTo(2.1));
        Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(video.AsSpan(16)), Is.EqualTo(7));
        await socket.SendAsync("audio-on"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
        byte[] audio = await ReceivePacket(socket);
        Assert.That(audio[0], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(audio.AsSpan(8)), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(audio.AsSpan(16)), Is.EqualTo(7));
        Assert.That(BinaryPrimitives.ReadInt16LittleEndian(audio.AsSpan(24)), Is.EqualTo(32767));
        Assert.That(BinaryPrimitives.ReadInt16LittleEndian(audio.AsSpan(26)), Is.EqualTo(-32767));
        // Neither media stream has acknowledged its packet. Pause must still be delivered.
        server.PublishStopped();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] control = new byte[256];
        var message = await socket.ReceiveAsync(control.AsMemory(), timeout.Token);
        Assert.That(message.MessageType, Is.EqualTo(WebSocketMessageType.Text));
        Assert.That(Encoding.UTF8.GetString(control, 0, message.Count), Does.Contain("\"playing\":false"));
        await socket.SendAsync("audio-ready"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        server.PublishAudio(new AudioFrameSnapshot([0.25f, 0.25f], 48000, 2, MediaTime.Zero) { PlaybackId = 8 });
        audio = await ReceivePacket(socket);
        Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(audio.AsSpan(16)), Is.EqualTo(8), "The next playback must replace old buffered audio.");
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
        using var decoded = SKBitmap.Decode(MobilePreviewServer.EncodeJpeg(bitmap, mode, exposure));
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
            using var jpeg = SKBitmap.Decode(MobilePreviewServer.EncodeJpeg(bitmap.Value, UIToneMappingOperator.Reinhard, 0));
            var expected = HeadlessTestHelpers.ReadPixel(screen, new Avalonia.PixelPoint(32, 32));
            SKColor actual = jpeg.GetPixel(16, 16);
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
            using var clean = SKBitmap.Decode(await ReceiveImage(socket));
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

    private static async Task<byte[]> ReceiveImage(ClientWebSocket socket)
        => (await ReceivePacket(socket))[PreviewMediaPacket.HeaderSize..];

    private static async Task<byte[]> ReceivePacket(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var image = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            var message = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            Assert.That(message.MessageType, Is.Not.EqualTo(WebSocketMessageType.Close));
            if (message.MessageType != WebSocketMessageType.Binary) continue;
            image.Write(buffer, 0, message.Count);
            if (message.EndOfMessage) return image.ToArray();
        }
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
