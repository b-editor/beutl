using System.Net;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Api.Services;
using Beutl.Services.VideoUpload;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class YouTubeVideoUploadTests
{
    [Test]
    public async Task Official_sdk_sends_metadata_and_file_in_chunks_and_requires_a_server_receipt()
    {
        byte[] bytes = new byte[8 * 1024 * 1024 + 17];
        new Random(42).NextBytes(bytes);
        string file = Path.Combine(Path.GetTempPath(), $"beutl-youtube-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(file, bytes);
        var received = new List<byte>();
        int chunks = 0;
        var handler = new Handler(async (request, token) =>
        {
            Assert.That(request.RequestUri!.Host, Is.EqualTo("youtube.googleapis.com"));
            if (request.Method == HttpMethod.Post)
            {
                JsonNode json = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                Assert.That(json["status"]!["privacyStatus"]!.GetValue<string>(), Is.EqualTo("private"));
                Assert.That(json["status"]!["selfDeclaredMadeForKids"]!.GetValue<bool>(), Is.False);
                Assert.That(json["snippet"]!["title"]!.GetValue<string>(), Is.EqualTo("Example"));
                var response = JsonResponse(HttpStatusCode.OK, "{}");
                response.Headers.Location = new Uri("https://youtube.googleapis.com/upload/youtube/v3/videos?upload_id=test");
                return response;
            }

            Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(request.Content!.Headers.ContentRange!.From, Is.EqualTo(received.Count));
            received.AddRange(await request.Content.ReadAsByteArrayAsync(token));
            chunks++;
            if (received.Count < bytes.Length)
            {
                var response = JsonResponse((HttpStatusCode)308, "{}");
                response.Headers.TryAddWithoutValidation("Range", $"bytes=0-{received.Count - 1}");
                return response;
            }
            return JsonResponse(HttpStatusCode.OK, "{\"id\":\"video-id\"}");
        });
        try
        {
            using var upload = new YouTubeVideoUploadOperation(file, Metadata(), () => Service(handler));
            Uri? result = await upload.UploadAsync(new Progress<double>(), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(result!.AbsoluteUri, Is.EqualTo("https://www.youtube.com/watch?v=video-id"));
                Assert.That(chunks, Is.EqualTo(2));
                Assert.That(received.ToArray(), Is.EqualTo(bytes));
                Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
            });
        }
        finally { File.Delete(file); }
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Sdk_failed_session_creation_is_not_reported_as_success(bool cancel)
    {
        string file = Path.Combine(Path.GetTempPath(), $"beutl-youtube-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(file, "local video");
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler((_, _) =>
        {
            if (cancel)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return Task.FromResult(JsonResponse(HttpStatusCode.Forbidden,
                "{\"error\":{\"code\":403,\"message\":\"quota exceeded\"}}"));
        });
        try
        {
            using var upload = new YouTubeVideoUploadOperation(file, Metadata(), () => Service(handler));
            Exception? failure = null;
            try { await upload.UploadAsync(new Progress<double>(), cancellation.Token); }
            catch (Exception ex) { failure = ex; }
            Assert.That(failure, Is.Not.Null);
            if (cancel) Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(File.ReadAllText(file), Is.EqualTo("local video"));
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTest]
    public void Metadata_requires_an_audience_defaults_to_private_and_never_serializes_authentication()
    {
        using var context = new YouTubeUploadContext();
        Assert.Throws<InvalidOperationException>(() => context.CreateVideo("video.mp4"));
        context.Audience.Value = context.Audiences[0];
        Video metadata = context.CreateVideo("my movie.mp4");
        context.Title.Value = "changed later";
        context.Visibility.Value = context.Visibilities[2];
        Assert.That(metadata.Snippet.Title, Is.EqualTo("my movie"));
        Assert.That(metadata.Status.PrivacyStatus, Is.EqualTo("private"));
        context.Description.Value = new string('あ', 1667);
        Assert.Throws<InvalidOperationException>(() => context.CreateVideo("movie.mp4"));
        var json = new JsonObject();
        context.WriteToJson(json);
        Assert.That(json.Select(x => x.Key), Is.EquivalentTo(new[] { "Title", "Description", "Visibility", "Audience" }));
    }

    [AvaloniaTest]
    [TestCase("en")]
    [TestCase("ja")]
    public void Description_validation_explains_byte_limits_and_recovers_with_manual_upload(string language)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(language);
        try
        {
            var registry = new ExtensionProvider();
            registry.AddExtensions(7710, [new YouTubeUploadExtension()]);
            using var vm = new VideoUploadViewModel(registry);
            vm.RecordCompletedFile(Path.Combine(Path.GetTempPath(), "reviewed.mp4"));
            var settings = (YouTubeUploadSettingsView)vm.SettingsControl.Value!;
            var context = (YouTubeUploadContext)settings.DataContext!;
            context.IsSignedIn.Value = true;
            context.Audience.Value = context.Audiences[0];
            var view = new VideoUploadView { DataContext = vm };
            using var host = new ControlHost(new Window
            {
                Width = 340,
                Height = 800,
                RequestedThemeVariant = language == "ja" ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            });
            var input = settings.FindControl<TextBox>("VideoDescriptionInput")!;
            var error = settings.FindControl<TextBlock>("DescriptionValidationError")!;
            var upload = view.FindControl<Button>("UploadButton")!;
            Assert.That(vm.Automatic.Value, Is.False);
            foreach (string boundary in new[] { new string('a', 5000), new string('あ', 1666) + "ab" })
            {
                input.Text = boundary + "c";
                HeadlessTestHelpers.Render();
                Assert.That(context.Description.Value, Is.EqualTo(boundary + "c"), "The input must not be silently truncated.");
                Assert.That(error.IsEffectivelyVisible, Is.True);
                Assert.That(error.Text, Does.Contain("5,000"));
                Assert.That(upload.IsEffectivelyEnabled, Is.False);
                Assert.Throws<InvalidOperationException>(() => context.CreateVideo("reviewed.mp4"));
                HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), $"description-error-{language}");

                input.Text = boundary;
                HeadlessTestHelpers.Render();
                Assert.That(error.IsVisible, Is.False);
                Assert.That(upload.IsEffectivelyEnabled, Is.True);
                Assert.That(context.CreateVideo("reviewed.mp4").Snippet.Description, Is.EqualTo(boundary));
            }
            input.Text = "<invalid>";
            HeadlessTestHelpers.Render();
            Assert.That(error.IsEffectivelyVisible, Is.True);
            Assert.That(upload.IsEffectivelyEnabled, Is.False);
            input.Text = "";
            HeadlessTestHelpers.Render();
            Assert.That(error.IsVisible, Is.False);
            Assert.That(upload.IsEffectivelyEnabled, Is.True);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [AvaloniaTest]
    [TestCase("en", 620, false)]
    [TestCase("en", 340, true)]
    [TestCase("ja", 620, false)]
    [TestCase("ja", 340, true)]
    public async Task Output_upload_sections_keep_settings_at_normal_and_narrow_widths(string language, int width, bool dark)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(language);
        const int packageId = 7710;
        try
        {
            TestGpu.RequireAnyDevice();
            var (_, _, editor) = await TestShell.OpenNewSceneAsync("upload-layout", 32, 32);
            var encoder = new TestEncoder();
            TestShell.Extensions.AddExtensions(packageId, [encoder]);
            using var output = new OutputViewModel(editor);
            output.SelectedEncoder.Value = encoder;
            output.DestinationFile.Value = Path.Combine(TestShell.ProjectRoot(editor), "next.mp4");
            var vm = output.Upload;
            vm.SelectedTarget.Value = vm.Targets.Single(x => x.TypeName.Contains(nameof(YouTubeUploadExtension)));
            var settings = (YouTubeUploadSettingsView)vm.SettingsControl.Value!;
            var context = (YouTubeUploadContext)settings.DataContext!;
            var view = new OutputView { DataContext = output };
            using var host = new ControlHost(new Window
            {
                Width = width,
                Height = 800,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            });
            HeadlessTestHelpers.Render();
            var sections = view.FindControl<TabStrip>("SectionStrip")!;
            var uploadPanel = view.FindControl<VideoUploadView>("UploadPanel")!;
            Assert.That(view.FindControl<StackPanel>("EncodingPanel")!.IsEffectivelyVisible, Is.True);
            Assert.That(uploadPanel.IsEffectivelyVisible, Is.False);
            HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), $"encode-{language}-{width}");

            sections.SelectedIndex = 1;
            HeadlessTestHelpers.Render();
            Assert.That(view.FindControl<StackPanel>("EncodingPanel")!.IsEffectivelyVisible, Is.False);
            Assert.That(uploadPanel.IsEffectivelyVisible, Is.True);
            Assert.That(uploadPanel.FindControl<StackPanel>("CompletedVideoPanel")!.IsVisible, Is.False);
            if (language == "ja" && width == 340)
                HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), "upload-empty");

            string filename = language == "ja"
                ? "週末の散歩と街の風景・確認済みの最終版.mp4" : "A weekend walk through the city - reviewed final version.mp4";
            vm.RecordCompletedFile(Path.Combine(TestShell.ProjectRoot(editor), filename));
            context.Title.Value = language == "ja" ? "週末の散歩" : "A weekend walk";
            context.Audience.Value = context.Audiences[0];
            HeadlessTestHelpers.Render();
            TextBox input = settings.FindControl<TextBox>("VideoTitleInput")!;
            Button upload = uploadPanel.FindControl<Button>("UploadButton")!;
            Assert.That(input.Bounds.Width, Is.GreaterThan(150));
            Assert.That(input.Bounds.Width, Is.LessThan(width));
            Assert.That(upload.IsEnabled, Is.False, "Authentication is required.");
            Assert.That(uploadPanel.FindControl<TextBlock>("CompletedFileName")!.Text, Is.EqualTo(filename));
            HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), $"upload-disconnected-{language}-{width}");

            // Only the connected presentation is simulated; no credential or network upload is created.
            context.IsSignedIn.Value = true;
            var automatic = view.FindControl<ToggleSwitch>("AutomaticUploadSwitch")!;
            automatic.IsChecked = true;
            sections.SelectedIndex = 0;
            sections.SelectedIndex = 1;
            HeadlessTestHelpers.Render();
            Assert.That(vm.Automatic.Value, Is.True);
            Assert.That(vm.SettingsControl.Value, Is.SameAs(settings), "Navigation must not recreate the authenticated context.");
            Assert.That(input.Text, Is.EqualTo(context.Title.Value));
            Assert.That(upload.IsEffectivelyEnabled, Is.True);
            Assert.That(automatic.OnContent, Is.EqualTo(Beutl.Language.Strings.On));
            Assert.That(settings.FindControl<Button>("ConnectButton")!.IsVisible, Is.False);
            Assert.That(settings.FindControl<TextBlock>("ConnectionStatus")!.Text, Is.EqualTo(Beutl.Language.Strings.VideoUpload_SignedIn));
            foreach (Control control in new Control[] { input, upload, settings.FindControl<ComboBox>("AudienceInput")! })
            {
                Point right = control.TranslatePoint(new Point(control.Bounds.Width, 0), view)!.Value;
                Assert.That(right.X, Is.LessThanOrEqualTo(view.Bounds.Width));
            }
            if (HeadlessCapture.IsRequested)
            {
                // Let the enabled-state brush transition finish before capturing the primary action.
                await Task.Delay(250);
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), $"upload-ready-{language}-{width}");
            }
            if (language == "ja" && width == 340)
            {
                context.IsSignedIn.Value = false;
                context.IsSigningIn.Value = true;
                HeadlessTestHelpers.Render();
                Assert.That(settings.FindControl<Button>("ConnectButton")!.IsVisible, Is.False);
                HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), "upload-signing-in");
                context.IsSigningIn.Value = false;
                context.ConnectionMessage.Value = Beutl.Language.Strings.VideoUpload_SignInFailed;
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(view, nameof(YouTubeVideoUploadTests), "upload-sign-in-error");
            }
        }
        finally
        {
            TestShell.Extensions.RemoveExtensions(packageId);
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    private static Video Metadata() => new()
    {
        Snippet = new VideoSnippet { Title = "Example", Description = "Description", CategoryId = "22" },
        Status = new VideoStatus { PrivacyStatus = "private", SelfDeclaredMadeForKids = false }
    };

    private static YouTubeService Service(HttpMessageHandler handler) => new(new BaseClientService.Initializer
    {
        ApplicationName = "Beutl-test",
        HttpClientFactory = new Factory(handler),
        GZipEnabled = false
    });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Factory(HttpMessageHandler handler) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handle(request, cancellationToken);
    }
}
