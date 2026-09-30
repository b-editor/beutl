using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GenerativeGraphRunnerTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-generative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Test]
    public async Task RunsUpstreamFirstAndFeedsItsResultDownstream()
    {
        var (model, upstream, downstream) = CreateChain();
        var executor = new FakeExecutor(_directory);
        var host = new InlineHost();

        await new GenerativeGraphRunner(executor, host).RunAsync(model, [downstream], force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { upstream, downstream }));
            var downstreamRequest = (AiImageGenerationNodeRequest)executor.Requests[1];
            Assert.That(downstreamRequest.References, Has.Count.EqualTo(1), "The upstream result is the downstream reference.");
            Assert.That(upstream.ActiveGeneration?.Image, Is.Not.Null);
            Assert.That(downstream.ActiveGeneration?.Image, Is.Not.Null);
            Assert.That(host.Commits, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ReusesResultsWhoseRequestIsUnchanged()
    {
        var (model, upstream, downstream) = CreateChain();
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost());
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        executor.Requests.Clear();

        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        Assert.That(executor.Requests, Is.Empty, "Nothing changed, so nothing is bought again.");

        downstream.Prompt.Property!.SetValue("a different prompt");
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { downstream }));
    }

    [Test]
    public async Task ForceRegeneratesOnlyTheTarget()
    {
        var (model, upstream, downstream) = CreateChain();
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost());
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        executor.Requests.Clear();

        await runner.RunAsync(model, [downstream], force: true, CancellationToken.None);

        Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { downstream }));
        Assert.That(downstream.Generations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FailureBlocksDependentsAndIsReported()
    {
        var (model, upstream, downstream) = CreateChain();
        var executor = new FakeExecutor(_directory) { FailFor = upstream };

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(upstream.Status, Is.EqualTo(GenerativeNodeStatus.Failed));
            Assert.That(upstream.StatusMessage, Is.EqualTo("refused"));
            Assert.That(downstream.Status, Is.EqualTo(GenerativeNodeStatus.Blocked));
            Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { upstream }));
        });
    }

    [Test]
    public async Task EmptyPromptFailsWithoutCallingTheExecutor()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        model.Nodes.Add(node);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(executor.Requests, Is.Empty);
        Assert.That(node.Status, Is.EqualTo(GenerativeNodeStatus.Failed));
    }

    [Test]
    public async Task SeedControlAdvancesTheSeedAfterGenerating()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        node.Seed.Property!.SetValue(10);
        node.SeedControl.Property!.SetValue(GenerativeSeedControl.Increment);
        model.Nodes.Add(node);

        await new GenerativeGraphRunner(new FakeExecutor(_directory), new InlineHost())
            .RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(node.ActiveGeneration?.Seed, Is.EqualTo(10));
        Assert.That(node.Seed.Property!.GetValue(), Is.EqualTo(11));
    }

    [Test]
    public async Task GenerationsSurviveSerialization()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        model.Nodes.Add(node);
        await new GenerativeGraphRunner(new FakeExecutor(_directory), new InlineHost())
            .RunAsync(model, null, force: false, CancellationToken.None);

        var json = CoreSerializer.SerializeToJsonObject(model);
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        var restoredNode = restored.Nodes.OfType<AiImageGenerationNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(restoredNode.RequestKeySeed, Is.EqualTo(node.RequestKeySeed));
            Assert.That(restoredNode.Generations, Has.Count.EqualTo(1));
            Assert.That(restoredNode.ActiveGeneration?.Fingerprint, Is.EqualTo(node.ActiveGeneration!.Fingerprint));
            Assert.That(restoredNode.ActiveGeneration?.Image?.Uri, Is.EqualTo(node.ActiveGeneration.Image!.Uri));
            Assert.That(restoredNode.IsStale, Is.False);
        });
    }

    [Test]
    public async Task PromptNodeSendsTheSectionsTheDialogWouldSend()
    {
        var model = new GraphModel();
        var prompt = new AiPromptNode();
        prompt.Prompt.Property!.SetValue("  a   cat ");
        prompt.Style.Property!.SetValue("watercolor");
        prompt.Exclusions.Property!.SetValue("text,\n watermark");
        var image = new AiImageGenerationNode();
        model.Nodes.Add(prompt);
        model.Nodes.Add(image);
        model.Connect(image.Prompt, prompt.Output);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        var request = (AiImageGenerationNodeRequest)executor.Requests.Single();
        Assert.That(request.Prompt, Is.EqualTo("a cat\nStyle: watercolor\nAvoid: text, watermark"));
        Assert.That(
            request.Prompt,
            Is.EqualTo(PromptSections.Compose("  a   cat ", "watercolor", exclusions: "text,\n watermark")));
    }

    [Test]
    public async Task EditingAConnectedPromptNodeMakesTheImageStale()
    {
        var model = new GraphModel();
        var prompt = new AiPromptNode();
        prompt.Prompt.Property!.SetValue("a cat");
        var image = new AiImageGenerationNode();
        model.Nodes.Add(prompt);
        model.Nodes.Add(image);
        model.Connect(image.Prompt, prompt.Output);
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost());
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        executor.Requests.Clear();

        prompt.Composition.Property!.SetValue("close-up");
        await runner.RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(executor.Requests, Has.Count.EqualTo(1));
        Assert.That(((AiImageGenerationNodeRequest)executor.Requests[0]).Prompt, Is.EqualTo("a cat\nComposition: close-up"));
    }

    [Test]
    public async Task PruneKeepsPinnedAndActiveGenerations()
    {
        var (model, node) = await GenerateTimesAsync(3);
        GenerationRecord first = node.Generations[0];
        first.IsPinned = true;

        int removed = node.PruneGenerations();

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(node.Generations, Is.EqualTo(new[] { first, node.ActiveGeneration }));
        });
    }

    [Test]
    public async Task SelectingAnEarlierGenerationIsUndoable()
    {
        var (model, node) = await GenerateTimesAsync(2);
        GenerationRecord first = node.Generations[0];
        GenerationRecord latest = node.Generations[1];
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(model, sequence);
        using var observer = new CoreObjectOperationObserver(null, model, sequence);
        history.Subscribe(observer);

        node.SelectGeneration(first.Id);
        history.Commit("select");
        Assert.That(node.ActiveGeneration, Is.SameAs(first));

        history.Undo();
        Assert.That(node.ActiveGeneration, Is.SameAs(latest));
    }

    [Test]
    public async Task PreviewMonitorShowsTheActiveGeneration()
    {
        var (model, node) = await GenerateTimesAsync(1);
        INodeMonitor preview = node.Items.OfType<INodeMonitor>().Single(m => m.Name == "GenerationPreview");
        preview.IsEnabled = true;

        // Never evaluated: the preview must not wait for the graph to be rendered.
        await node.PreviewLoad;

        var value = ((NodeMonitor<Beutl.Media.Source.Ref<Bitmap>?>)preview).Value;
        Assert.That(value?.Value.Width, Is.EqualTo(8));
    }

    [Test]
    public async Task PreviewIsBusyWhileGeneratingAndLoadingUntilTheResultIsShown()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        model.Nodes.Add(node);
        var preview = (NodeMonitor<Beutl.Media.Source.Ref<Bitmap>?>)node.Items.OfType<INodeMonitor>()
            .Single(m => m.Name == "GenerationPreview");
        preview.IsEnabled = true;
        var busyWhileRunning = new List<(bool, string?)>();
        var executor = new FakeExecutor(_directory)
        {
            OnExecute = () => busyWhileRunning.Add((preview.IsBusy, preview.BusyText)),
        };

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(busyWhileRunning, Is.EqualTo(new[] { (true, (string?)null) }), "A bare ring while generating.");

        // Without the graph ever being evaluated, the loading state ends once the file is read.
        await node.PreviewLoad;

        Assert.That(preview.IsBusy, Is.False);
        Assert.That(preview.BusyText, Is.Null);
        Assert.That(preview.Value?.Value.Width, Is.EqualTo(8));
    }

    [Test]
    public async Task EditNodeEditsTheUpstreamGenerationAndFollowsItsTask()
    {
        var model = new GraphModel();
        var image = new AiImageGenerationNode();
        image.Prompt.Property!.SetValue("a cat");
        var edit = new AiImageEditNode();
        model.Nodes.Add(image);
        model.Nodes.Add(edit);
        model.Connect(edit.Source, image.Output);
        edit.Prompt.Property!.SetValue("ignored for this task");
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, [edit], force: false, CancellationToken.None);

        var request = (AiImageEditNodeRequest)executor.Requests[1];
        Assert.Multiple(() =>
        {
            Assert.That(executor.Requests[0].Node, Is.SameAs(image));
            Assert.That(request.Image.EncodedPng, Is.Not.Empty, "The upstream result is the picture edited.");
            Assert.That(request.Task, Is.EqualTo(AiImageEditTask.RemoveBackground));
            Assert.That(request.Prompt, Is.Null, "Removing a background takes no prompt.");
            Assert.That(request.CatalogOperationId, Is.EqualTo("image.edit.remove_background"));
        });

        int changes = 0;
        edit.CatalogOperationChanged += (_, _) => changes++;
        edit.Task.Property!.SetValue(AiImageEditTask.Outpaint);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(edit.CatalogOperationId, Is.EqualTo("image.edit.outpaint"));
    }

    [Test]
    public async Task EditTasksThatTakeAPromptRequireOne()
    {
        var model = new GraphModel();
        var source = new AiImageGenerationNode();
        source.Prompt.Property!.SetValue("a cat");
        var edit = new AiImageEditNode();
        edit.Task.Property!.SetValue(AiImageEditTask.Restyle);
        model.Nodes.Add(source);
        model.Nodes.Add(edit);
        model.Connect(edit.Source, source.Output);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(edit.Status, Is.EqualTo(GenerativeNodeStatus.Failed));
        Assert.That(edit.StatusMessage, Is.EqualTo(Beutl.Language.Strings.AiPromptRequired));
        Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { source }));
    }

    [Test]
    public void EditNodeWithoutASourceFailsWithTheTabsMessage()
    {
        var edit = new AiImageEditNode();
        var model = new GraphModel();
        model.Nodes.Add(edit);
        var executor = new FakeExecutor(_directory);

        new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.That(edit.StatusMessage, Is.EqualTo(Beutl.Language.Strings.AiEditSelectSource));
        Assert.That(executor.Requests, Is.Empty);
    }

    [Test]
    public async Task VideoNodeUsesAnUpstreamPictureAsItsFirstFrameAndSetsReferencesAside()
    {
        var model = new GraphModel();
        var image = new AiImageGenerationNode();
        image.Prompt.Property!.SetValue("a cat");
        var reference = new AiImageGenerationNode();
        reference.Prompt.Property!.SetValue("a dog");
        var video = new AiVideoGenerationNode();
        video.Prompt.Property!.SetValue("the cat walks");
        model.Nodes.AddRange([image, reference, video]);
        model.Connect(video.FirstFrame, image.Output);
        model.Connect(video.ImageReferences, reference.Output);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, [video], force: false, CancellationToken.None);

        var request = executor.Requests.OfType<AiVideoGenerationNodeRequest>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.FirstFrame, Is.Not.Null);
            Assert.That(request.ImageReferences, Is.Empty, "As in the AI tab, a first frame sets references aside.");
            Assert.That(request.DurationSeconds, Is.EqualTo(6));
            Assert.That(request.Resolution, Is.EqualTo("720p"));
            Assert.That(request.AspectRatio, Is.EqualTo("16:9"));
            Assert.That(request.GenerateAudio, Is.True);
            Assert.That(video.ActiveGeneration?.Video, Is.Not.Null);
            Assert.That(video.ActiveGeneration?.Image, Is.Null);
        });
    }

    [Test]
    public async Task VideoNodeSendsReferencesWithoutAFirstFrameAndRejectsALoneLastFrame()
    {
        var model = new GraphModel();
        var reference = new AiImageGenerationNode();
        reference.Prompt.Property!.SetValue("a dog");
        var video = new AiVideoGenerationNode();
        video.Prompt.Property!.SetValue("the dog runs");
        model.Nodes.AddRange([reference, video]);
        model.Connect(video.ImageReferences, reference.Output);
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost());

        await runner.RunAsync(model, [video], force: false, CancellationToken.None);
        Assert.That(executor.Requests.OfType<AiVideoGenerationNodeRequest>().Single().ImageReferences, Has.Count.EqualTo(1));

        model.Disconnect(video.ImageReferences.Connections.Single().Value!);
        model.Connect(video.LastFrame, reference.Output);
        await runner.RunAsync(model, [video], force: true, CancellationToken.None);
        Assert.That(video.Status, Is.EqualTo(GenerativeNodeStatus.Failed));
    }

    [Test]
    public async Task VideoEditNodeExtendsAClipAVideoNodeJustGenerated()
    {
        var model = new GraphModel();
        var video = new AiVideoGenerationNode();
        video.Prompt.Property!.SetValue("a cat walks");
        var extend = new AiVideoEditNode();
        extend.Task.Property!.SetValue(AiVideoEditMode.Extend);
        extend.Prompt.Property!.SetValue("and then sits");
        model.Nodes.AddRange([video, extend]);
        model.Connect(extend.Source, video.Output);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, [extend], force: false, CancellationToken.None);

        var request = executor.Requests.OfType<AiVideoEditNodeRequest>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.SourceVideo.Content, Is.EqualTo(FakeExecutor.Clip), "The generated clip is the source.");
            Assert.That(request.SourceVideo.MediaType, Is.EqualTo("video/mp4"));
            Assert.That(request.Mode, Is.EqualTo(AiVideoEditMode.Extend));
            Assert.That(request.CatalogOperationId, Is.EqualTo("video.extend"));
            Assert.That(extend.ActiveGeneration?.Video, Is.Not.Null);
        });
    }

    [Test]
    public async Task MotionNeedsACharacterImage()
    {
        var model = new GraphModel();
        var video = new AiVideoGenerationNode();
        video.Prompt.Property!.SetValue("a dancer");
        var motion = new AiVideoEditNode();
        motion.Task.Property!.SetValue(AiVideoEditMode.Motion);
        motion.Prompt.Property!.SetValue("dance like this");
        model.Nodes.AddRange([video, motion]);
        model.Connect(motion.Source, video.Output);

        await new GenerativeGraphRunner(new FakeExecutor(_directory), new InlineHost())
            .RunAsync(model, [motion], force: false, CancellationToken.None);

        Assert.That(motion.StatusMessage, Is.EqualTo(Beutl.Language.Strings.AiChooseCharacterImage));
    }

    [Test]
    public async Task ANewOrSelectedGenerationAsksTheSceneToRenderAgain()
    {
        var (model, node) = await GenerateTimesAsync(1);
        GenerationRecord first = node.ActiveGeneration!;
        int edits = 0;
        node.Edited += (_, _) => edits++;

        await new GenerativeGraphRunner(new FakeExecutor(_directory), new InlineHost())
            .RunAsync(model, [node], force: true, CancellationToken.None);
        Assert.That(edits, Is.GreaterThan(0), "A new result must reach the canvas.");

        edits = 0;
        node.SelectGeneration(first.Id);
        Assert.That(edits, Is.GreaterThan(0), "Switching results must reach the canvas.");
    }

    [Test]
    public async Task UnreadableResultStillEndsTheLoadingState()
    {
        var (_, node) = await GenerateTimesAsync(1);
        await node.PreviewLoad;
        var record = new GenerationRecord { Image = new Beutl.Media.Source.ImageSource() };
        record.Image.ReadFrom(new Uri(Path.Combine(_directory, "missing.png")));
        node.Generations.Add(record);

        node.SelectGeneration(record.Id);
        await node.PreviewLoad;

        var preview = (NodeMonitor<Beutl.Media.Source.Ref<Bitmap>?>)node.Items.OfType<INodeMonitor>()
            .Single(m => m.Name == "GenerationPreview");
        Assert.That(preview.IsBusy, Is.False);
        Assert.That(preview.Value, Is.Null);
    }

    [Test]
    public async Task ASceneNodeFeedsAnAiNodeAndAnUnchangedSceneIsNotBoughtAgain()
    {
        var referenced = new Beutl.ProjectSystem.Scene(64, 48, "referenced")
        {
            Uri = new Uri(Path.Combine(_directory, "referenced.scene")),
        };
        var rect = new Beutl.Graphics.Shapes.RectShape();
        rect.Width.CurrentValue = 32;
        rect.Height.CurrentValue = 32;
        rect.Fill.CurrentValue = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.Red);
        var element = new Beutl.ProjectSystem.Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(10),
            Uri = new Uri(Path.Combine(_directory, "rect.layer")),
        };
        element.AddObject(rect);
        referenced.Children.Add(element);

        var model = new GraphModel();
        var scene = new Beutl.NodeGraph.Nodes.SceneNode();
        scene.Object.ReferencedScene.CurrentValue = referenced;
        var edit = new AiImageEditNode();
        model.Nodes.AddRange([scene, edit]);
        model.Connect(edit.Source, scene.Output);
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost { CurrentTime = TimeSpan.FromSeconds(1) });

        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        Assert.That(((AiImageEditNodeRequest)executor.Requests.Single()).Image.EncodedPng, Is.Not.Empty,
            "The scene as it looks at the playhead is the picture edited.");

        executor.Requests.Clear();
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        Assert.That(executor.Requests, Is.Empty, "An unchanged scene is not bought again.");

        rect.Width.CurrentValue = 16;
        await runner.RunAsync(model, null, force: false, CancellationToken.None);
        Assert.That(executor.Requests, Has.Count.EqualTo(1), "A changed scene is.");
    }

    [TestCase(null, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task ASettledRequestStartsANewIdempotencyKey(bool? failureSettles, bool renewed)
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        model.Nodes.Add(node);
        string before = node.RequestKeySeed;
        var executor = new FakeExecutor(_directory) { FailureSettles = failureSettles ?? false };
        if (failureSettles is not null)
            executor.FailFor = node;

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.That(node.RequestKeySeed != before, Is.EqualTo(renewed),
            "A success or a settled failure needs a new key; an unsettled one keeps the way back to a paid job.");
    }

    [Test]
    public async Task VariationsKeepEverySeedAndLeaveTheLastOneActiveAndCurrent()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        node.Seed.Property!.SetValue(100);
        model.Nodes.Add(node);
        var executor = new FakeExecutor(_directory);
        var runner = new GenerativeGraphRunner(executor, new InlineHost());

        await runner.RunAsync(model, [node], force: false, CancellationToken.None, variations: 3);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Requests.Cast<AiImageGenerationNodeRequest>().Select(r => r.Seed),
                Is.EqualTo(new int?[] { 100, 101, 102 }));
            Assert.That(executor.Requests.Select(r => r.RequestKeySeed).Distinct().Count(), Is.EqualTo(3),
                "Each variation goes out under its own key.");
            Assert.That(node.Generations.Select(g => g.Seed), Is.EqualTo(new int?[] { 100, 101, 102 }));
            Assert.That(node.ActiveGeneration, Is.SameAs(node.Generations[2]));
            Assert.That(node.Seed.Property!.GetValue(), Is.EqualTo(102), "The inputs reproduce the kept variation.");
        });

        // The active variation matches the inputs, so it is not stale and a plain run buys nothing.
        executor.Requests.Clear();
        using (var snapshot = new GraphSnapshot())
        {
            snapshot.Build(model, CompositionContext.Default);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        }

        Assert.That(node.IsStale, Is.False);
        await runner.RunAsync(model, [node], force: false, CancellationToken.None);
        Assert.That(executor.Requests, Is.Empty);
    }

    [Test]
    public async Task AiNodesInsideAGroupRunBetweenWhatFeedsAndReadsTheGroup()
    {
        // A group only mirrors its inner ports once it is attached, as in the editor.
        using var scene = new SceneHistoryHarness("generative-group");
        var application = new BeutlApplication();
        application.Items.Add(scene.Scene);
        var model = new GraphModel();
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        scene.AddElement().AddObject(drawable);
        var upstream = new AiImageGenerationNode();
        upstream.Prompt.Property!.SetValue("a cat");
        var downstream = new AiImageGenerationNode();
        downstream.Prompt.Property!.SetValue("the same cat, in watercolor");
        var group = new GroupNode();
        var input = new GroupInput();
        var output = new GroupOutput();
        var edit = new AiImageEditNode();
        edit.Task.Property!.SetValue(AiImageEditTask.RemoveBackground);
        // Listed first so only the dependencies, not the node order, put it in the middle.
        model.Nodes.AddRange([downstream, group, upstream]);
        group.Group.Nodes.AddRange([input, output, edit]);
        input.AddNodePort(edit.Source, out _);
        output.AddNodePort(edit.Output, out _);
        model.Connect(group.Items.OfType<IInputPort>().Single(), upstream.Output);
        model.Connect(downstream.References, group.Items.OfType<IOutputPort>().Single());
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { upstream, edit, downstream }));
            Assert.That(edit.ActiveGeneration?.Image, Is.Not.Null);
            Assert.That(((AiImageGenerationNodeRequest)executor.Requests[2]).References, Has.Count.EqualTo(1),
                "The group's generated output reaches the node after it.");
        });

        // Run from the group's own graph, as its tab does: the graph around it still feeds it.
        executor.Requests.Clear();
        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(group.Group, [edit], force: true, CancellationToken.None);
        Assert.That(executor.Requests.Select(r => r.Node), Is.EqualTo(new GenerativeNode[] { edit }));
    }

    [Test]
    public async Task VariationsPaidForBeforeAFailureAreCommitted()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        node.Seed.Property!.SetValue(100);
        node.SeedControl.Property!.SetValue(GenerativeSeedControl.Increment);
        model.Nodes.Add(node);
        var executor = new FakeExecutor(_directory) { FailAfter = 2 };
        var host = new InlineHost();

        await new GenerativeGraphRunner(executor, host).RunAsync(model, [node], force: false, CancellationToken.None, variations: 3);

        Assert.Multiple(() =>
        {
            Assert.That(node.Generations, Has.Count.EqualTo(2));
            Assert.That(host.Commits, Is.EqualTo(1), "The kept results are one undoable, saved step.");
            Assert.That(node.Status, Is.EqualTo(GenerativeNodeStatus.Failed));
            Assert.That(node.Seed.Property!.GetValue(), Is.EqualTo(101),
                "The seed control runs first, so the inputs still reproduce the active variation.");
        });
    }

    [Test]
    public async Task ProgressDeliveredAfterTheRunEndedIsDropped()
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        model.Nodes.Add(node);
        var executor = new FakeExecutor(_directory);

        await new GenerativeGraphRunner(executor, new InlineHost()).RunAsync(model, null, force: false, CancellationToken.None);
        executor.LastProgress!.Report(new GenerativeProgress("late"));

        Assert.Multiple(() =>
        {
            Assert.That(node.Status, Is.EqualTo(GenerativeNodeStatus.Idle));
            Assert.That(node.StatusMessage, Is.Null);
        });
    }

    private async Task<(GraphModel Model, AiImageGenerationNode Node)> GenerateTimesAsync(int count)
    {
        var model = new GraphModel();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        model.Nodes.Add(node);
        var runner = new GenerativeGraphRunner(new FakeExecutor(_directory), new InlineHost());
        for (int i = 0; i < count; i++)
            await runner.RunAsync(model, [node], force: true, CancellationToken.None);
        return (model, node);
    }

    private static (GraphModel Model, AiImageGenerationNode Upstream, AiImageGenerationNode Downstream) CreateChain()
    {
        var model = new GraphModel();
        var upstream = new AiImageGenerationNode();
        upstream.Prompt.Property!.SetValue("a cat");
        var downstream = new AiImageGenerationNode();
        downstream.Prompt.Property!.SetValue("the same cat, in watercolor");
        model.Nodes.Add(upstream);
        model.Nodes.Add(downstream);
        model.Connect(downstream.References, upstream.Output);
        return (model, upstream, downstream);
    }

    private sealed class FakeExecutor(string directory) : IGenerativeNodeExecutor
    {
        public List<GenerativeRequest> Requests { get; } = [];

        public GenerativeNode? FailFor { get; set; }

        public bool FailureSettles { get; init; }

        public Action? OnExecute { get; init; }

        /// <summary>Fails every request after this many succeeded.</summary>
        public int? FailAfter { get; init; }

        public IProgress<GenerativeProgress>? LastProgress { get; private set; }

        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastProgress = progress;
            OnExecute?.Invoke();
            if (ReferenceEquals(request.Node, FailFor) || Requests.Count > FailAfter)
                throw new GenerativeExecutionException("refused") { SettledRequest = FailureSettles };

            bool video = request is AiVideoGenerationNodeRequest or AiVideoEditNodeRequest;
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}{(video ? ".mp4" : ".png")}");
            if (video)
            {
                File.WriteAllBytes(path, Clip);
            }
            else
            {
                using var bitmap = new Bitmap(8, 8);
                using var stream = File.Create(path);
                bitmap.Save(stream, EncodedImageFormat.Png);
            }
            int? seed = (request as AiImageGenerationNodeRequest)?.Seed;
            return Task.FromResult(new GenerativeExecutionResult(
                new Uri(path), request.ModelId, seed, IsVideo: video));
        }

        public static readonly byte[] Clip = [0, 0, 0, 24, 102, 116, 121, 112];
    }

    private sealed class InlineHost : IGenerativeRunHost
    {
        public int Commits { get; private set; }

        public TimeSpan CurrentTime { get; set; } = TimeSpan.Zero;

        public Task<T> InvokeOnRenderThreadAsync<T>(Func<T> func) => Task.FromResult(func());

        public Task InvokeOnUIThreadAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void CommitHistory(string name) => Commits++;
    }
}
