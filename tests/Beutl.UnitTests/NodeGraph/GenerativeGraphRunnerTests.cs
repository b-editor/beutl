using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Graphics;
using Beutl.NodeGraph.Composition;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.Serialization;

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

        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

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
        Assert.That(preview.IsBusy, Is.True, "Generated, but not shown yet.");
        Assert.That(preview.BusyText, Is.EqualTo(Beutl.Language.NodeGraphStrings.Generative_Loading));

        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

        Assert.That(preview.IsBusy, Is.False);
        Assert.That(preview.BusyText, Is.Null);
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

        public GenerativeNode? FailFor { get; init; }

        public Action? OnExecute { get; init; }

        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            OnExecute?.Invoke();
            if (ReferenceEquals(request.Node, FailFor))
                throw new GenerativeExecutionException("refused");

            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            using (var bitmap = new Bitmap(8, 8))
            using (var stream = File.Create(path))
                bitmap.Save(stream, EncodedImageFormat.Png);
            var image = (AiImageGenerationNodeRequest)request;
            return Task.FromResult(new GenerativeExecutionResult(new Uri(path), request.ModelId, image.Seed));
        }
    }

    private sealed class InlineHost : IGenerativeRunHost
    {
        public int Commits { get; private set; }

        public TimeSpan CurrentTime => TimeSpan.Zero;

        public Task<T> InvokeOnRenderThreadAsync<T>(Func<T> func) => Task.FromResult(func());

        public Task InvokeOnUIThreadAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void CommitHistory(string name) => Commits++;
    }
}
