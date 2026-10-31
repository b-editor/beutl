using Beutl.Audio;
using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.PreviewStreaming;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
public sealed class MobilePreviewTests
{
    [TestCase(6)]
    [TestCase(8)]
    public void SampleAudio_FoldsCenterAndSurrounds(int channels)
    {
        var input = new float[960 * channels];
        for (int i = 0; i < 960; i++)
        {
            input[i * channels + 2] = 0.25f;
            input[i * channels + 4] = 0.125f;
        }
        var snapshot = new Beutl.Editor.Models.AudioFrameSnapshot(input, 48000, channels, MediaTime.Zero) { PlaybackId = 7 };
        var output = MobilePreviewServer.SampleAudio([snapshot], 7, 0)!;
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.GetDefault(channels), AudioChannelLayout.Stereo);
        Assert.That(output[0], Is.EqualTo(0.25f * matrix.GetCoefficient(0, 2) + 0.125f * matrix.GetCoefficient(0, 4)));
        Assert.That(output[1], Is.EqualTo(0.25f * matrix.GetCoefficient(1, 2)));
    }

    [TestCase(600, "10:00")]
    [TestCase(59.1, "1:00")]
    [TestCase(0.1, "0:01")]
    [TestCase(0, null)]
    [TestCase(-1, null)]
    public void Pairing_expiry_uses_remaining_time_across_midnight(double remainingSeconds, string? remaining)
    {
        var expires = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(9));
        var now = expires.ToUniversalTime().AddSeconds(-remainingSeconds);
        string expected = remaining is null ? Beutl.Language.Strings.MobilePreviewCodeExpired
            : string.Format(Beutl.Language.Strings.MobilePreviewExpires, remaining);
        Assert.That(MobilePreviewTabViewModel.FormatExpiry(expires, now), Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task Pairing_expiry_updates_while_sharing_and_renewing_the_code_resets_it()
    {
        await using var host = await RelayHost.StartAsync();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-preview-expiry");
        var mobile = new MobilePreviewTabViewModel(editor);
        var view = new MobilePreviewTabView { DataContext = mobile };
        var owner = new Window { Content = view, Width = 300, Height = 420 };
        try
        {
            owner.Show();
            Assert.That(editor.OpenToolTab(mobile), Is.True);
            await mobile.StartSharingAsync(host.Session);
            await host.AcceptAsync();
            string initialExpiry = mobile.Expiry.Value;
            Assert.That(mobile.Expiry.Value, Is.Not.EqualTo(Beutl.Language.Strings.MobilePreviewCodeExpired));
            await HeadlessTestHelpers.WaitUntilAsync(() => mobile.Expiry.Value != initialExpiry);
            await mobile.StopSharingAsync();
            await mobile.StartSharingAsync(host.Session with { CodeExpires = DateTimeOffset.UtcNow.AddSeconds(-1) });
            var relay = await host.AcceptAsync();
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<Avalonia.Controls.TextBlock>("ExpiryText")!.Text, Is.EqualTo(Beutl.Language.Strings.MobilePreviewCodeExpired));
            HeadlessCapture.SaveIfRequested(owner, nameof(MobilePreviewTests), "desktop-code-expired");

            mobile.RenewCode();
            Assert.That(mobile.Expiry.Value, Is.EqualTo(string.Format(Beutl.Language.Strings.MobilePreviewExpires, "10:00")));
            var pairing = await ReadMessage(relay, m => m.GetProperty("type").GetString() == "pairing");
            Assert.That(pairing.GetProperty("expires").GetInt64(), Is.EqualTo(mobile.Server!.CodeExpires.ToUnixTimeMilliseconds()));
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(owner, nameof(MobilePreviewTests), "desktop-code-renewed");
        }
        finally
        {
            editor.CloseToolTab(mobile);
            await mobile.StopSharingAsync();
            owner.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [Test]
    public async Task Signaling_clients_keep_only_the_latest_bitmap_and_disconnect_releases_it()
    {
        await using var host = await RelayHost.StartAsync();
        await using var server = await MobilePreviewServer.StartAsync(host.Session);
        using var red = SolidFrame(255, 0, 0);
        server.Publish(red, UIToneMappingOperator.None, 0, false);
        Assert.That(red.RefCount, Is.EqualTo(1));
        var socket = await host.AcceptAsync();
        await Roster(socket, ["one"]);
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
        await Roster(socket, []);
        await WaitUntil(() => server.GetClients().Length == 0 && blue.RefCount == 1);
        Assert.That(server.GetDevices(), Is.Empty);
    }

    [Test]
    public async Task Relay_keeps_other_viewers_alive_after_malformed_signals_and_lists_offline_devices()
    {
        await using var host = await RelayHost.StartAsync();
        await using var server = await MobilePreviewServer.StartAsync(host.Session);
        var relay = await host.AcceptAsync();
        Task Send(object message) => SendMessage(relay, message);
        var devices = new[] { new { id = "one", address = "Phone 1" }, new { id = "two", address = "Phone 2" } };
        await Send(new { clients = devices, paired = devices });
        await WaitUntil(() => server.GetClients().Length == 2);
        Assert.That(server.GetDevices().All(d => d.Connected), Is.True);
        for (int i = 0; i < 1000; i++) server.PublishPlayback(true, i / 10.0, 0, 120, 30, true);
        server.RenewPairingCode();
        var pairing = await ReadMessage(relay, _ => true);
        Assert.That(pairing.GetProperty("type").GetString(), Is.EqualTo("pairing"),
            "Frequent playback updates must never enter the billed relay, even before WebRTC is ready.");
        Assert.That(pairing.GetProperty("code").GetString(), Is.EqualTo(server.PairingCode));

        server.ControlRequested = (id, _, _) => Task.FromResult(id == "two");
        foreach (string message in new[] { "null", "[]", "{\"type\":false}", "{\"type\":\"offer\"}",
                     "{\"type\":\"offer\",\"sdp\":17}", "{\"type\":\"candidate\"}",
                     "{\"type\":\"offer\",\"sdp\":\"invalid SDP\",\"hdr\":\"false\"}",
                     "{\"type\":\"candidate\",\"candidate\":{\"candidate\":17}}" })
            await Send(new { type = "signal", id = "one", signal = JsonSerializer.Deserialize<JsonElement>(message) });

        Task Offer(string id, string connectionId, object iceServers) => Send(new
        {
            type = "signal",
            id,
            signal = new { type = "offer", connectionId, sdp = PreviewTransportTests.Offer, iceServers }
        });
        async Task AssertAnswer(string connectionId)
        {
            var answer = await ReadSignal(relay, m => m.GetProperty("type").GetString() == "answer"
                && m.GetProperty("connectionId").GetString() == connectionId);
            Assert.That(answer.GetProperty("sdp").GetString(), Does.Contain("a=rtpmap:98 VP8/90000"));
            Assert.That(answer.GetProperty("hdr").GetBoolean(), Is.False);
        }
        await Offer("two", "connected", Array.Empty<PreviewIceServer>());
        await AssertAnswer("connected");
        foreach (string ice in new[] { "{}", "null", "[null]", "[{}]", "[{\"urls\":null}]", "[{\"urls\":[null]}]", "[{\"urls\":[17]}]" })
        {
            await Offer("one", "malformed", JsonSerializer.Deserialize<JsonElement>(ice));
            var error = await ReadSignal(relay, m => m.GetProperty("type").GetString() == "error"
                && m.GetProperty("connectionId").GetString() == "malformed");
            Assert.That(error.GetProperty("message").GetString(), Is.EqualTo("negotiation-failed"));
        }
        foreach (object candidate in new object[] { new { candidate = 17 }, "invalid", new { candidate = "" } })
            await Send(new { type = "signal", id = "two", signal = new { type = "candidate", connectionId = "connected", candidate } });
        await Offer("one", "recovered", Array.Empty<PreviewIceServer>());
        await AssertAnswer("recovered");
        await Send(new { type = "signal", id = "two", signal = new { type = "control", requestId = 1, action = "play" } });
        var result = await ReadMessage(relay, m => m.GetProperty("type").GetString() == "signal"
            && m.GetProperty("signal").GetProperty("type").GetString() == "control-result");
        Assert.That(result.GetProperty("id").GetString(), Is.EqualTo("two"));
        Assert.That(result.GetProperty("signal").GetProperty("ok").GetBoolean(), Is.True);

        await Send(new { clients = devices[1..], paired = devices });
        await WaitUntil(() => server.GetClients().Length == 1);
        Assert.That(server.GetDevices().Single(d => d.Id == "one").Connected, Is.False);
        server.Disconnect("one");
        var disconnect = await ReadMessage(relay, m => m.GetProperty("type").GetString() == "disconnect");
        Assert.That(disconnect.GetProperty("id").GetString(), Is.EqualTo("one"));
        await Send(new { clients = devices[1..], paired = devices[1..] });
        await WaitUntil(() => server.GetDevices().Length == 1);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RelayDisconnected += (_, _) => disconnected.TrySetResult();
        await relay.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(server.GetClients(), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Audio_continues_between_low_rate_video_frames_and_stops_on_pause(int frameRate)
    {
        var blocks = new[] { new AudioFrameSnapshot(Enumerable.Repeat(0.25f, 48000 * 2).ToArray(),
            48000, 2, MediaTime.Zero) { PlaybackId = 7 } };
        using var frame = new MobilePreviewServer.PendingFrame(SolidFrame(0, 0, 0), UIToneMappingOperator.None,
            0, true, 0, 7, 0);
        long nextSample = 0;
        var samples = new float[960 * 2];
        for (int packet = 0; packet < 50 / frameRate; packet++)
        {
            Assert.That(MobilePreviewServer.SampleCurrentAudio(blocks, frame, packet * 0.02, ref nextSample, samples), Is.True,
                $"Audio packet {packet} between {frameRate} fps frames.");
            Assert.That(samples, Is.All.EqualTo(0.25f));
            nextSample += 960;
        }
        Assert.That(MobilePreviewServer.SampleCurrentAudio(blocks, frame with { Playing = false }, 1, ref nextSample, samples), Is.False);
        Assert.That(nextSample, Is.Zero);
        Assert.That(MobilePreviewServer.SampleCurrentAudio(blocks, frame with { PlaybackId = 8 }, 1, ref nextSample, samples), Is.False);
    }

    [Test]
    public async Task Republishing_a_frame_preserves_audio_time_but_new_playback_resets_it()
    {
        await using var host = await RelayHost.StartAsync();
        await using var server = await MobilePreviewServer.StartAsync(host.Session);
        var relay = await host.AcceptAsync();
        await Roster(relay, ["one"]);
        await WaitUntil(() => server.GetClients().Length == 1);
        using var image = SolidFrame(0, 0, 0);
        var latest = typeof(MobilePreviewServer).GetField("_latest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        MobilePreviewServer.PendingFrame Frame() => (MobilePreviewServer.PendingFrame)latest.GetValue(server)!;

        server.Publish(image, UIToneMappingOperator.None, 0, true, 0, 7);
        double clock = Frame().Clock;
        await Task.Delay(100);
        server.Publish(image, UIToneMappingOperator.ACES, 1, true, 0, 7);
        Assert.That(Frame().Clock, Is.EqualTo(clock));
        var samples = Enumerable.Repeat(0.25f, 24000 * 2).Concat(Enumerable.Repeat(0.5f, 24000 * 2)).ToArray();
        var blocks = new[] { new AudioFrameSnapshot(samples, 48000, 2, MediaTime.Zero) { PlaybackId = 7 } };
        long nextSample = (long)((clock + 0.55) * 48000);
        var packet = new float[960 * 2];
        Assert.That(MobilePreviewServer.SampleCurrentAudio(blocks, Frame(), clock + 0.55, ref nextSample, packet), Is.True);
        Assert.That(packet, Is.All.EqualTo(0.5f));

        server.Publish(image, UIToneMappingOperator.None, 0, true, 1, 7);
        Assert.That(Frame().Clock, Is.GreaterThan(clock), "A new video frame reanchors the audio timeline.");
        clock = Frame().Clock;
        await Task.Delay(20);
        server.Publish(image, UIToneMappingOperator.None, 0, true, 1, 8);
        Assert.That(Frame().Clock, Is.GreaterThan(clock), "A new playback session must not inherit the old clock.");
        server.PublishStopped();
        clock = Frame().Clock;
        await Task.Delay(20);
        server.Publish(image, UIToneMappingOperator.None, 0, true, 1, 8);
        Assert.That(Frame().Clock, Is.GreaterThan(clock), "Resuming playback must reset the paused clock.");
    }

    [Test]
    public void Audio_selects_the_visible_time_and_playback_and_crosses_block_boundaries()
    {
        var blocks = new[] {
            new AudioFrameSnapshot(Enumerable.Repeat(0.25f, 48000 * 2).ToArray(), 48000, 2, MediaTime.Zero) { PlaybackId = 7 },
            new AudioFrameSnapshot(Enumerable.Repeat(0.5f, 48000 * 2).ToArray(), 48000, 2, MediaTime.FromSeconds(1)) { PlaybackId = 7 },
            new AudioFrameSnapshot(Enumerable.Repeat(-0.25f, 48000 * 2).ToArray(), 48000, 2, MediaTime.Zero) { PlaybackId = 8 }
        };
        var sample = new float[960 * 2];
        Assert.That(MobilePreviewServer.SampleAudio(blocks, 7, 0.99, sample), Is.True);
        Assert.That(sample[0], Is.EqualTo(0.25f));
        Assert.That(sample[^1], Is.EqualTo(0.5f));
        Assert.That(MobilePreviewServer.SampleAudio(blocks, 8, 0, sample), Is.True);
        Assert.That(sample[0], Is.EqualTo(-0.25f));
        Assert.That(MobilePreviewServer.SampleAudio(blocks, 9, 0, sample), Is.False);
    }

    [Test]
    public void Hdr_conversion_preserves_highlights_above_reference_white()
    {
        using var bitmap = new Bitmap(32, 32, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        var (width, height) = MobilePreviewServer.FrameSize(bitmap);
        using var converter = new MobilePreviewServer.FrameConverter(width, height, hdr: true);
        bitmap.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)1, (Half)1, (Half)1, (Half)1));
        ushort white = BinaryPrimitives.ReadUInt16LittleEndian(converter.Convert(bitmap, UIToneMappingOperator.ACES, 0));
        bitmap.GetPixelSpan<RgbaF16>().Fill(new RgbaF16((Half)4, (Half)4, (Half)4, (Half)1));
        ushort highlight = BinaryPrimitives.ReadUInt16LittleEndian(converter.Convert(bitmap, UIToneMappingOperator.ACES, 0));
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
        await using var host = await RelayHost.StartAsync();
        TestGpu.RequireAnyDevice();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-preview", 320, 240);
        var owner = new Window { Content = new EditView { DataContext = editor }, Width = 1100, Height = 800 };
        var presentation = new PresentationWindow(editor.Player);
        MobilePreviewTabViewModel? mobile = null;
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
            HeadlessTestHelpers.Render();
            var playerView = owner.GetVisualDescendants().OfType<PlayerView>().Single();
            void OpenMobile()
            {
                var button = playerView.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == Beutl.Language.Strings.PresentationWindow);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HeadlessTestHelpers.Render();
                var popup = owner.OpenedPopups.Single(popup => popup.Child is MenuFlyoutPresenter);
                HeadlessCapture.SaveIfRequested(owner, nameof(MobilePreviewTests), "projection-menu");
                var item = ((MenuFlyoutPresenter)popup.Child!).Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, Beutl.Language.Strings.MobilePreview));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                popup.IsOpen = false;
            }
            OpenMobile();
            mobile = editor.FindToolTab<MobilePreviewTabViewModel>()!;
            Assert.That(mobile, Is.Not.Null);
            Assert.That(mobile.Extension.Header, Is.Null);
            HeadlessTestHelpers.Render();
            MobilePreviewTabView View() => owner.GetVisualDescendants().OfType<MobilePreviewTabView>().Single();
            void AssertPage(string name)
            {
                foreach (string page in new[] { "StartPage", "ConnectionPage", "DetailsPage" })
                    Assert.That(View().FindControl<ScrollViewer>(page)!.IsEffectivelyVisible, Is.EqualTo(page == name), page);
            }
            void Capture(string name)
            {
                HeadlessCapture.SaveIfRequested(owner, nameof(MobilePreviewTests), name);
                if (!HeadlessCapture.IsRequested || !closePresentationFirst) return;
                var theme = owner.RequestedThemeVariant;
                owner.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(owner, nameof(MobilePreviewTests), name + "-dark");
                owner.RequestedThemeVariant = theme;
                HeadlessTestHelpers.Render();
            }
            AssertPage("StartPage");
            Capture("desktop-ready");
            var start = View().FindControl<Button>("StartButton")!;
            var icon = View().FindControl<FluentIcons.Avalonia.Fluent.FluentIcon>("StartIcon")!;
            var progress = View().FindControl<Beutl.Controls.ProgressRing>("StartProgress")!;
            var buttonSize = start.Bounds.Size;
            mobile.IsBusy.Value = true;
            await Task.Delay(250);
            HeadlessTestHelpers.Render();
            Assert.That(start.IsEnabled, Is.False);
            Assert.That(icon.IsEffectivelyVisible, Is.False);
            Assert.That(progress.IsEffectivelyVisible && progress.IsIndeterminate, Is.True);
            Assert.That(start.Bounds.Size, Is.EqualTo(buttonSize));
            Capture("desktop-starting");
            mobile.IsBusy.Value = false;
            HeadlessTestHelpers.Render();
            Assert.That(start.IsEnabled && icon.IsEffectivelyVisible, Is.True);
            Assert.That(progress.IsEffectivelyVisible, Is.False);
            presentation.ShowOnScreen(owner, null, false);
            OpenMobile();
            Assert.That(editor.FindToolTab<MobilePreviewTabViewModel>(), Is.SameAs(mobile));
            Assert.That(editor.DockHost.Factory.EnumerateTools().Count(t => t.ToolContext is MobilePreviewTabViewModel), Is.EqualTo(1));
            await mobile.StartSharingAsync(host.Session);
            var server = mobile.Server!;
            var socket = await host.AcceptAsync();
            HeadlessTestHelpers.Render();
            AssertPage("ConnectionPage");
            Assert.That(View().FindControl<Button>("DetailsButton")!.IsEffectivelyVisible, Is.False);
            Capture("desktop-waiting");
            await Roster(socket, ["one"]);
            // The server roster changes before the UI-thread callback switches the page.
            await HeadlessTestHelpers.WaitUntilAsync(() => mobile.ShowDetails.Value);
            HeadlessTestHelpers.Settle();
            AssertPage("DetailsPage");
            Assert.That(View().FindControl<Button>("DetailsButton")!.IsEffectivelyVisible, Is.False);
            editor.Player.QueuePreviewRender();
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
            using var cleanFrame = editor.Player.GetCleanPreviewFrame().Image!;
            using var clean = SdrBitmap(cleanFrame.Value, UIToneMappingOperator.None, 0);
            Assert.That(clean.Pixels.Count(p => p.Red > 200 && p.Green < 60 && p.Blue < 60), Is.GreaterThan(0));
            Assert.That(clean.Pixels.Count(p => p.Red > 240 && p.Green > 240 && p.Blue > 240), Is.Zero);
            Capture("desktop-sharing");
            Assert.That(editor.OpenToolTab(new PreviewSettingsTabViewModel(editor)), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(mobile.Server, Is.SameAs(server), "Selecting another tool must keep sharing alive.");
            OpenMobile();
            HeadlessTestHelpers.Render();
            AssertPage("DetailsPage");
            View().FindControl<Button>("AddDeviceButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            AssertPage("ConnectionPage");
            Assert.That(View().FindControl<Button>("DetailsButton")!.IsEffectivelyVisible, Is.True);
            Assert.That(View().FindControl<Image>("QrImage")!.Source, Is.SameAs(mobile.QrImage.Value));

            await Roster(socket, [], ["one"]);
            await HeadlessTestHelpers.WaitUntilAsync(() => server.GetClients().Length == 0);
            HeadlessTestHelpers.Settle();
            AssertPage("ConnectionPage");
            View().FindControl<Button>("DetailsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            AssertPage("DetailsPage");
            Capture("desktop-offline-device");
            await Roster(socket, ["one"]);
            await HeadlessTestHelpers.WaitUntilAsync(() => server.GetClients().Length == 1);
            // The mobile player acquires the clean preview in a UI-thread post raised after the server's client
            // list changes, so wait for its consumer next to the presentation window's, not for the client count.
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.CleanPreviewConsumerCount == 2);
            HeadlessTestHelpers.Settle();

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
            HeadlessTestHelpers.Render();
            AssertPage("StartPage");

            await mobile.StartSharingAsync(host.Session);
            Assert.That(mobile.Server, Is.Not.SameAs(server));
            HeadlessTestHelpers.Render();
            AssertPage("ConnectionPage");
            Assert.That(View().FindControl<Button>("DetailsButton")!.IsEffectivelyVisible, Is.False);
            if (closePresentationFirst) TestShell.Editor.SelectedTabItem.Value = null;
            else editor.CloseToolTab(mobile);
            await mobile.StopSharingAsync();
            Assert.That(mobile.IsSharing.Value, Is.False);
            Assert.That(mobile.Server, Is.Null);
        }
        finally
        {
            if (mobile is not null)
            {
                editor.CloseToolTab(mobile);
                await mobile.StopSharingAsync();
            }
            presentation.Close();
            owner.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Paired_controls_follow_the_player_and_seek_safely_during_playback()
    {
        await using var host = await RelayHost.StartAsync();
        TestGpu.RequireAnyDevice();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-controls", 320, 240);
        var owner = new Window();
        var mobile = new MobilePreviewTabViewModel(editor);
        try
        {
            await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(120));
            owner.Show();
            Assert.That(editor.OpenToolTab(mobile), Is.True);
            await mobile.StartSharingAsync(host.Session);
            var server = mobile.Server!;
            var socket = await host.AcceptAsync();
            await Roster(socket, ["one"]);
            await HeadlessTestHelpers.WaitUntilAsync(() => server.GetClients().Length == 1, pump: false);

            editor.Player.CurrentFrame.Value = MediaTime.FromSeconds(1);
            int request = 0;
            async Task<bool> Control(string action, double position = 0)
            {
                int id = ++request;
                await SendMessage(socket, new { type = "signal", id = "one", signal = new { type = "control", requestId = id, action, position } });
                var result = await ReadSignal(socket, m => m.GetProperty("type").GetString() == "control-result" && m.GetProperty("requestId").GetInt32() == id);
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
            editor.CloseToolTab(mobile);
            await mobile.StopSharingAsync();
            owner.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [Test]
    public async Task Disconnecting_a_viewer_cancels_its_queued_controls()
    {
        await using var host = await RelayHost.StartAsync();
        await using var server = await MobilePreviewServer.StartAsync(host.Session);
        var relay = await host.AcceptAsync();
        await Roster(relay, ["one", "two"]);
        await WaitUntil(() => server.GetClients().Length == 2);
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
        Task Control(string id, int requestId) => SendMessage(relay, new
        {
            type = "signal",
            id,
            signal = new { type = "control", requestId, action = "next" }
        });
        try
        {
            await Control("one", 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Control("two", 2);
            server.Disconnect("two");
            release.TrySetResult();
            await Control("one", 3);
            await ReadSignal(relay, m => m.GetProperty("type").GetString() == "control-result" && m.GetProperty("requestId").GetInt32() == 3);
            Assert.That(applied, Is.EqualTo(2), "The revoked device's queued command must not run; the remaining device still works.");
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaTest]
    public async Task Closing_during_startup_leaves_no_relay_or_preview_consumer()
    {
        await using var host = await RelayHost.StartAsync();
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("mobile-preview-cancel");
        var owner = new Window();
        var mobile = new MobilePreviewTabViewModel(editor);
        try
        {
            owner.Show();
            Assert.That(editor.OpenToolTab(mobile), Is.True);
            Task start = mobile.StartSharingAsync(host.Session);
            editor.CloseToolTab(mobile);
            try { await start; } catch (OperationCanceledException) { }
            await mobile.StopSharingAsync();
            Assert.That(mobile.Server, Is.Null);
            Assert.That(editor.Player.IsCleanPreviewEnabled, Is.False);
        }
        finally { owner.Close(); await TestReset.ResetShellAsync(); }
    }

    private static async Task SendMessage(WebSocket socket, object message)
        => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, CancellationToken.None);

    private static Task Roster(WebSocket socket, string[] connected, string[]? paired = null)
        => SendMessage(socket, new
        {
            clients = connected.Select(id => new { id, address = "Phone " + id }),
            paired = (paired ?? connected).Select(id => new { id, address = "Phone " + id })
        });

    private static async Task<JsonElement> ReadSignal(WebSocket socket, Func<JsonElement, bool> predicate)
    {
        var message = await ReadMessage(socket, m => m.GetProperty("type").GetString() == "signal" && predicate(m.GetProperty("signal")));
        return message.GetProperty("signal").Clone();
    }

    // Only the test host listens on HTTP. It supplies authenticated publisher signaling,
    // while the production desktop owns neither pairing endpoints nor viewer assets.
    private sealed class RelayHost(WebApplication app) : IAsyncDisposable
    {
        private readonly Channel<WebSocket> _sockets = Channel.CreateUnbounded<WebSocket>();
        private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal PreviewSessionResponse Session { get; private set; } = null!;

        internal static async Task<RelayHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var host = new RelayHost(app);
            app.UseWebSockets();
            app.Run(async context =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await host._sockets.Writer.WriteAsync(socket);
                await host._stop.Task;
            });
            await app.StartAsync();
            host.Session = new PreviewSessionResponse(new Uri("https://beutl.beditor.net/preview/" + new string('a', 64) + "/" + Guid.NewGuid() + "/"),
                new UriBuilder(app.Urls.Single()) { Scheme = "ws" }.Uri, "test-publisher", "123456", DateTimeOffset.UtcNow.AddMinutes(10));
            return host;
        }

        internal async Task<WebSocket> AcceptAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await _sockets.Reader.ReadAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.TrySetResult();
            await app.DisposeAsync();
        }
    }

    private static async Task<JsonElement> ReadMessage(WebSocket socket, Func<JsonElement, bool> predicate)
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
        using var converter = new MobilePreviewServer.FrameConverter(width, height, hdr: false);
        byte[] pixels = converter.Convert(image, mode, exposure);
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
