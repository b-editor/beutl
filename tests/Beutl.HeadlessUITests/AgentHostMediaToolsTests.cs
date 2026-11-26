using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.FFmpegIpc;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.ViewModels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using Moq;
using SkiaSharp;
using static Beutl.HeadlessUITests.AgentHostTestHelpers;

namespace Beutl.HeadlessUITests;

[NonParallelizable] // Registers a proxy generator in the process-wide extension registry.
public sealed class AgentHostMediaToolsTests
{
    [AvaloniaTest]
    public async Task All_media_tools_require_a_scene_in_the_live_tool_schema()
    {
        string directory = CreateDirectory("agent-media-schema-");
        await using var host = CreateHost(TestShell.Editor, directory, Guid.NewGuid().ToString("N"));
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        var tools = await client.ListToolsAsync();
        var expected = new Dictionary<string, string[]>
        {
            ["generate_proxies"] = ["elementIds", "mediaPaths", "preset"],
            ["read_proxy_status"] = ["elementIds", "mediaPaths", "preset", "jobIds"],
            ["remove_proxies"] = ["elementIds", "mediaPaths", "preset"],
            ["scan_missing_media"] = [],
            ["relink_missing_media"] = ["replacements", "searchFolder"]
        };

        foreach (var (name, inputs) in expected)
        {
            JsonElement schema = tools.Single(tool => tool.Name == name).JsonSchema;
            Assert.Multiple(() =>
            {
                Assert.That(schema.GetProperty("required").EnumerateArray().Select(property => property.GetString()),
                    Is.EquivalentTo(new[] { "sceneId" }), name);
                Assert.That(schema.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                    Is.EquivalentTo(inputs.Concat(["sceneId", "instanceId"])), name);
            });
            AssertError(await client.CallToolAsync(name), ErrorCode.ValidationRejected);
        }
    }

    [AvaloniaTest]
    public async Task Live_relink_saves_the_scene_and_keeps_the_editors_undo_stack()
    {
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("Agent repair", duration: MediaTime.FromSeconds(2));
        string directory = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string missing = Path.Combine(directory, "missing.png");
        string replacement = Path.Combine(directory, "replacement.png");
        using (var image = new SKBitmap(8, 8))
        {
            image.Erase(SKColors.Coral);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(replacement, png.ToArray());
        }
        var clip = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        clip.Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "Image.belm"));
        var imageObject = new SourceImage { Source = { Value = ImageSource.Open(missing) } };
        clip.Objects.Add(imageObject);
        scene.Children.Add(clip);
        editor.HistoryManager.Commit("Add image");
        int undoCount = editor.HistoryManager.UndoCount;
        await using var host = CreateHost(TestShell.Editor, directory, Guid.NewGuid().ToString("N"));
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);

        var target = Target(scene);
        var scan = Success(await client.CallToolAsync("scan_missing_media", target));
        Assert.That(scan["missing"]!.AsArray().Single()!["originalPath"]!.GetValue<string>(), Is.EqualTo(missing));
        target["replacements"] = new[] { new { missingPath = missing, newPath = replacement } };
        var repair = Success(await client.CallToolAsync("relink_missing_media", target));
        var stored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(repair["relinked"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(repair["remainingMissing"]!.AsArray(), Is.Empty);
            Assert.That(imageObject.Source.Value!.Uri.LocalPath, Is.EqualTo(replacement));
            Assert.That(((SourceImage)stored.Children.Single().Objects.Single()).Source.Value!.Uri.LocalPath, Is.EqualTo(replacement));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            Assert.That(editor.IsEnabled.Value, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Removing_a_proxy_cancels_the_active_encode_and_keeps_the_original()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new GeneratorFactory((_, _) => new DelegateGenerator(async job =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, job.CancellationToken);
        }));
        await WithProxyProjectAsync(factory, async (client, proxy, scene, clip, path) =>
        {
            var fingerprint = ProxyFingerprint.FromFile(path);
            string relative = "existing.mp4";
            File.Copy(path, Path.Combine(proxy.Store.StoreRootPath, relative));
            proxy.Store.Register(new ProxyEntry(fingerprint, ProxyPreset.Quarter, ProxyState.Ready, relative,
                fingerprint.FileSizeBytes, new PixelSize(96, 64), new PixelSize(24, 16), DateTime.UtcNow, DateTime.UtcNow, null));
            var byPath = Target(scene);
            byPath["mediaPaths"] = new[] { path };
            byPath["preset"] = "quarter";
            Assert.That((await ReadProxyAsync(client, byPath))["state"]!.GetValue<string>(), Is.EqualTo("ready"));

            var byElement = Target(scene);
            byElement["elementIds"] = new[] { clip.Id.Value.ToString() };
            byElement["preset"] = "quarter";
            var queued = Success(await client.CallToolAsync("generate_proxies", byElement).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            string jobId = queued["jobs"]!.AsArray().Single()!["jobId"]!.GetValue<string>();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var byJob = Target(scene);
            byJob["jobIds"] = new[] { jobId };
            Assert.That((await ReadJobAsync(client, byJob))["status"]!.GetValue<string>(), Is.EqualTo("running"));

            var removed = Success(await client.CallToolAsync("remove_proxies", byPath));
            var completed = await ReadJobAsync(client, byJob);
            var status = await ReadProxyAsync(client, byElement);
            Assert.Multiple(() =>
            {
                Assert.That(removed["removed"]!.AsArray(), Has.Count.EqualTo(1));
                Assert.That(removed["canceledJobIds"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo(jobId));
                Assert.That(completed["status"]!.GetValue<string>(), Is.EqualTo("canceled"));
                Assert.That(status["state"]!.GetValue<string>(), Is.EqualTo("missing"));
                Assert.That(File.Exists(path), Is.True);
            });
        });
    }

    [AvaloniaTest]
    public async Task Status_and_removal_by_path_cover_cached_and_queued_variants_of_an_unreferenced_video()
    {
        var factory = new GeneratorFactory((_, _) => new DelegateGenerator(async job =>
            await Task.Delay(Timeout.InfiniteTimeSpan, job.CancellationToken)));
        await WithProxyProjectAsync(factory, async (client, proxy, scene, _, path) =>
        {
            string unreferenced = Path.Combine(Path.GetDirectoryName(path)!, "unreferenced.mp4");
            File.Copy(path, unreferenced);
            var fingerprint = ProxyFingerprint.FromFile(unreferenced);
            var cachedVariant = new VideoDeinterlaceOptions(DeinterlaceMode.On, DeinterlaceFieldOrder.Auto,
                DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
            var queuedVariant = cachedVariant with { FieldOrder = DeinterlaceFieldOrder.TopFieldFirst };
            var halfVariant = cachedVariant with { FieldOrder = DeinterlaceFieldOrder.BottomFieldFirst };
            foreach (ProxyPreset preset in new[] { ProxyPreset.Quarter, ProxyPreset.Half })
            {
                string relative = $"{preset}.mp4";
                File.Copy(path, Path.Combine(proxy.Store.StoreRootPath, relative));
                proxy.Store.Register(new ProxyEntry(fingerprint, preset, ProxyState.Ready, relative,
                    fingerprint.FileSizeBytes, new PixelSize(96, 64), new PixelSize(24, 16), DateTime.UtcNow, DateTime.UtcNow, null)
                {
                    Deinterlace = preset == ProxyPreset.Quarter ? cachedVariant : halfVariant
                });
            }

            ProxyJob job = await proxy.Queue.EnqueueAsync(fingerprint, ProxyPreset.Quarter, deinterlace: queuedVariant);
            var arguments = Target(scene);
            arguments["mediaPaths"] = new[] { unreferenced };
            arguments["preset"] = "quarter";

            var status = Success(await client.CallToolAsync("read_proxy_status", arguments))
                .Deserialize<ProxyTools.StatusResponse>(McpJsonUtilities.DefaultOptions)!;
            Assert.Multiple(() =>
            {
                Assert.That(status.Proxies.Select(proxy => (proxy.Deinterlace, proxy.Preset, proxy.State)),
                    Is.EquivalentTo(new[] { (cachedVariant, "quarter", "ready"), (queuedVariant, "quarter", "missing") }));
                Assert.That(status.Jobs.Select(found => (found.JobId, found.Deinterlace)), Is.EqualTo(new[] { (job.JobId, queuedVariant) }));
            });

            var result = Success(await client.CallToolAsync("remove_proxies", arguments));
            Assert.Multiple(() =>
            {
                Assert.That(result["removed"]!.AsArray(), Has.Count.EqualTo(1));
                Assert.That(result["canceledJobIds"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo(job.JobId.ToString()));
                Assert.That(proxy.Store.TryGet(fingerprint, ProxyPreset.Quarter, cachedVariant), Is.Null);
                Assert.That(proxy.Store.TryGet(fingerprint, ProxyPreset.Half, halfVariant), Is.Not.Null);
            });
        });
    }

    [AvaloniaTest]
    public async Task A_short_video_generates_a_ready_proxy_and_the_job_remains_pollable()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            Assert.Ignore("FFmpeg worker binary is unavailable.");

        try { await Task.Run(() => FFmpegWorkerProcess.DecodingInstance.EnsureStarted()); }
        catch (FFmpegLibrariesNotFoundException ex) { Assert.Ignore($"FFmpeg natives unavailable ({ex.Message})."); }

        var factory = new GeneratorFactory((store, decoders) => new FFmpegProxyGenerator(store, TestShell.Notifications, decoders));
        await WithProxyProjectAsync(factory, async (client, proxy, scene, clip, path) =>
        {
            var arguments = Target(scene);
            arguments["mediaPaths"] = new[] { path, path };
            var queued = Success(await client.CallToolAsync("generate_proxies", arguments));
            string jobId = queued["jobs"]!.AsArray().Single()!["jobId"]!.GetValue<string>();
            var poll = Target(scene);
            poll["jobIds"] = new[] { jobId };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            JsonNode job;
            do
            {
                job = await ReadJobAsync(client, poll, deadline.Token);
                if (job["status"]!.GetValue<string>() is not ("queued" or "running")) break;
                await Task.Delay(50, deadline.Token);
            }
            while (true);

            Assert.That(job["status"]!.GetValue<string>(), Is.EqualTo("succeeded"), job.ToJsonString());
            var entry = proxy.Store.TryGet(ProxyFingerprint.FromFile(path), ProxyPreset.Quarter)!;
            var byElement = Target(scene);
            byElement["elementIds"] = new[] { clip.Id.Value.ToString() };
            byElement["preset"] = "quarter";
            var status = await ReadProxyAsync(client, byElement);
            Assert.Multiple(() =>
            {
                Assert.That(entry.State, Is.EqualTo(ProxyState.Ready));
                Assert.That(File.Exists(Path.Combine(proxy.Store.StoreRootPath, entry.ProxyFileRelative)), Is.True);
                Assert.That(entry.ProxyDecodedFrameSize.Width, Is.LessThan(entry.OriginalLogicalFrameSize.Width));
                Assert.That(status["state"]!.GetValue<string>(), Is.EqualTo("ready"));
                Assert.That(job["progress"]!.GetValue<double>(), Is.EqualTo(1));
            });
        });
    }

    private static async Task WithProxyProjectAsync(IProxyGeneratorFactory factory,
        Func<McpClient, ProxyMediaServices, Scene, Element, string, Task> test)
    {
        await TestReset.ResetShellAsync();
        string directory = TestShell.NewWorkspace("agent-proxy-" + Guid.NewGuid().ToString("N"));
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(Path.Combine(directory, "Proxy.bep"),
            640, 480, new Rational(30), TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        string path = Path.Combine(directory, "video.mp4");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "deinterlace-offset-tff.mp4"), path);
        var clip = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        clip.Objects.Add(new SourceVideo { Source = { Value = VideoSource.Open(path) } });
        scene.Children.Add(clip);
        ProjectOperations.Save(project);

        var config = new GlobalConfiguration();
        config.ProxyStoreConfig.StoreRootPath.Value = Path.Combine(directory, "proxy-store");
        var decoders = new DecoderRegistry(config.ExtensionConfig);
        decoders.Register(new FFmpegDecoderInfo(new FFmpegDecodingSettings()));
        await using var proxy = new ProxyMediaServices(config, TestShell.Notifications, decoders);
        var services = new Mock<IServiceProvider>();
        services.Setup(provider => provider.GetService(It.IsAny<Type>())).Returns<Type>(type =>
            type == typeof(ProxyMediaServices) ? proxy
            : type == typeof(DecoderRegistry) ? decoders
            : type == typeof(GlobalConfiguration) ? config
            : TestShell.MainViewModel.AppServices.GetService(type));
        var editor = new EditorService(TestShell.MainViewModel.ExtensionProvider, services.Object);
        ProxyGeneratorRegistry.Register(factory);
        try
        {
            editor.ActivateTabItem(scene);
            await using var host = CreateHost(editor, directory, Guid.NewGuid().ToString("N"));
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            await test(client, proxy, scene, clip, path);
            Assert.That(((EditViewModel)editor.SelectedTabItem.Value!.Context.Value).HistoryManager.UndoCount, Is.Zero);
        }
        finally
        {
            if (editor.TryGetTabItem(scene, out var item)) await editor.CloseTabItem(item, saveChanges: false);
            ProxyGeneratorRegistry.Unregister(factory);
        }
    }

    private static Dictionary<string, object?> Target(Scene scene) => new() { ["sceneId"] = scene.Id.Value.ToString() };

    private static async Task<JsonNode> ReadProxyAsync(McpClient client, Dictionary<string, object?> arguments)
        => Success(await client.CallToolAsync("read_proxy_status", arguments))["proxies"]!.AsArray().Single()!;

    private static async Task<JsonNode> ReadJobAsync(McpClient client, Dictionary<string, object?> arguments, CancellationToken token = default)
        => Success(await client.CallToolAsync("read_proxy_status", arguments, cancellationToken: token))["jobs"]!.AsArray().Single()!;

    private sealed class GeneratorFactory(Func<IProxyStore, DecoderRegistry, IProxyGenerator> create) : IProxyGeneratorFactory
    {
        public IProxyGenerator Create(IProxyStore store, DecoderRegistry decoders) => create(store, decoders);
    }

    private sealed class DelegateGenerator(Func<ProxyJob, ValueTask> generate) : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job) => generate(job);
    }
}
