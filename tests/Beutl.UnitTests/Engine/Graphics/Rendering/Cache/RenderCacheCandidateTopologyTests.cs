using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

/// <summary>
/// Pins <see cref="RenderCacheResolver.BuildCandidateTopology"/> to the pair-wise reachability
/// semantics it replaced. The topology drives cache-candidate supersedence, so a divergence here
/// silently changes which candidates are cached rather than failing loudly.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RenderCacheCandidateTopologyTests
{
    private static readonly PixelSize s_frameSize = new(240, 160);
    private static readonly Rect s_syntheticBounds = new(0, 0, 64, 64);

    private static readonly string[] s_graphs =
    [
        "RepresentativeScene",
        "Shapes5",
        "Shapes10",
        "Shapes25",
        "Shapes50",
        "Shapes100",
        "SharedFragmentId",
        "DiamondInputs",
        "DeepChain",
        "DisconnectedRoots",
    ];

    private static GraphCase CreateGraph(string name) => name switch
    {
        "RepresentativeScene" => RepresentativeScene(),
        "Shapes5" => ShapeScene(5),
        "Shapes10" => ShapeScene(10),
        "Shapes25" => ShapeScene(25),
        "Shapes50" => ShapeScene(50),
        "Shapes100" => ShapeScene(100),
        "SharedFragmentId" => SharedFragmentIdCandidates(),
        "DiamondInputs" => DiamondInputs(),
        "DeepChain" => DeepChain(),
        "DisconnectedRoots" => DisconnectedRoots(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [TestCaseSource(nameof(s_graphs))]
    public void BuildCandidateTopology_MatchesThePairWiseReference(string name)
    {
        GraphCase graphCase = RenderThread.Dispatcher.Invoke(() => CreateGraph(name));
        try
        {
            RenderCacheResolver.CandidateTopology actual = RenderCacheResolver.BuildCandidateTopology(
                graphCase.Graph,
                graphCase.References);
            ReferenceTopology expected = BuildReferenceTopology(graphCase.Graph, graphCase.References);

            TestContext.Out.WriteLine(
                $"fragments={graphCase.Graph.Fragments.Length} candidates={graphCase.Graph.CacheCandidates.Length} " +
                $"descendantPairs={expected.Descendants.Values.Sum(static set => set.Count)}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    actual.Descendants.Keys,
                    Is.EquivalentTo(expected.Descendants.Keys),
                    "every candidate must get exactly one descendant set");
                foreach ((RenderCacheCandidateId parent, HashSet<RenderCacheCandidateId> reference)
                         in expected.Descendants)
                {
                    Assert.That(
                        actual.Descendants[parent],
                        Is.EquivalentTo(reference),
                        $"descendants of {parent} must match the pair-wise reference");
                }

                Assert.That(actual.ParentFirst, Has.Length.EqualTo(expected.ParentFirst.Length));
                for (int index = 0; index < Math.Min(actual.ParentFirst.Length, expected.ParentFirst.Length); index++)
                {
                    Assert.That(
                        actual.ParentFirst[index],
                        Is.SameAs(expected.ParentFirst[index]),
                        $"parent-first entry {index} must match, so tie-breaking stays observable");
                }
            });
        }
        finally
        {
            graphCase.Dispose();
        }
    }

    [Test]
    public void BuildCandidateTopology_DoesNotGrowQuadraticallyWithCandidateCount()
    {
        using GraphCase small = RenderThread.Dispatcher.Invoke(() => ShapeScene(25));
        using GraphCase large = RenderThread.Dispatcher.Invoke(() => ShapeScene(100));

        long smallBytes = MeasureTopologyBytes(small);
        long largeBytes = MeasureTopologyBytes(large);
        double candidateRatio = (double)large.Graph.CacheCandidates.Length / small.Graph.CacheCandidates.Length;
        double byteRatio = (double)largeBytes / smallBytes;

        TestContext.Out.WriteLine(
            $"candidates {small.Graph.CacheCandidates.Length} -> {large.Graph.CacheCandidates.Length} " +
            $"({candidateRatio:F2}x), bytes {smallBytes} -> {largeBytes} ({byteRatio:F2}x)");

        Assert.That(
            byteRatio,
            Is.LessThan(candidateRatio * candidateRatio / 2),
            "one traversal per candidate must keep topology allocation well below the pair-wise quadratic");
    }

    private static long MeasureTopologyBytes(GraphCase graphCase)
    {
        for (int round = 0; round < 3; round++)
            _ = RenderCacheResolver.BuildCandidateTopology(graphCase.Graph, graphCase.References);

        long best = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = RenderCacheResolver.BuildCandidateTopology(graphCase.Graph, graphCase.References);
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return best;
    }

    private sealed record ReferenceTopology(
        Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>> Descendants,
        RenderCacheCandidate[] ParentFirst);

    /// <summary>
    /// The implementation this fixture pins, transcribed from the revision that ran one full DFS per
    /// ordered candidate pair. Kept verbatim so a divergence is attributable to the production change.
    /// </summary>
    private static ReferenceTopology BuildReferenceTopology(
        RecordedRenderGraph graph,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> references)
    {
        var result = new Dictionary<RenderCacheCandidateId, HashSet<RenderCacheCandidateId>>();
        foreach (RenderCacheCandidate parent in graph.CacheCandidates)
        {
            var descendants = new HashSet<RenderCacheCandidateId>();
            foreach (RenderCacheCandidate child in graph.CacheCandidates)
            {
                if (parent.Id == child.Id)
                    continue;
                if (parent.FragmentId == child.FragmentId)
                {
                    if (parent.AuthoredOrder > child.AuthoredOrder)
                        descendants.Add(child.Id);
                    continue;
                }

                if (DependsOn(references[parent.FragmentId], references[child.FragmentId]))
                    descendants.Add(child.Id);
            }

            result.Add(parent.Id, descendants);
        }

        RenderCacheCandidate[] parentFirst = [.. graph.CacheCandidates
            .OrderByDescending(candidate => result[candidate.Id].Count)
            .ThenByDescending(static candidate => candidate.AuthoredOrder)];
        return new ReferenceTopology(result, parentFirst);
    }

    private static bool DependsOn(
        RenderFragmentReference parent,
        RenderFragmentReference possibleDescendant)
    {
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<RenderFragmentReference>(parent.Inputs);
        while (pending.TryPop(out RenderFragmentReference? current))
        {
            if (ReferenceEquals(current, possibleDescendant))
                return true;
            if (!visited.Add(current))
                continue;
            foreach (RenderFragmentReference input in current.Inputs)
                pending.Push(input);
        }

        return false;
    }

    private sealed class GraphCase(
        RecordedRenderGraph graph,
        Dictionary<RenderFragmentId, RenderFragmentReference> references,
        IDisposable[] owned) : IDisposable
    {
        public RecordedRenderGraph Graph { get; } = graph;

        public Dictionary<RenderFragmentId, RenderFragmentReference> References { get; } = references;

        public void Dispose()
        {
            foreach (IDisposable disposable in owned)
                disposable.Dispose();
        }
    }

    private static GraphCase RepresentativeScene()
        => RecordDrawables(RenderCacheCandidateTopologyScenes.CreateRepresentativeScene());

    private static GraphCase ShapeScene(int shapes)
        => RecordDrawables(RenderCacheCandidateTopologyScenes.CreateShapeScene(shapes, s_frameSize));

    private static GraphCase RecordDrawables(Drawable.Resource[] resources)
    {
        var root = new DrawableRenderNode(resources[0]);
        using (var context = new GraphicsContext2D(root, s_frameSize.ToSize(1)))
        {
            context.Clear();
            foreach (Drawable.Resource resource in resources)
                context.DrawDrawable(resource);
        }

        WarmCaches(root, []);
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            cachePolicy: RenderCacheOptions.Enabled,
            targetDomain: new Rect(default, s_frameSize.ToSize(1))));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        Assert.That(graph.CacheCandidates, Is.Not.Empty, "the recorded scene must produce cache candidates");
        return new GraphCase(graph, IndexReferences(graph), [request, root, .. resources]);
    }

    private static void WarmCaches(RenderNode current, HashSet<RenderNode> seen)
    {
        if (current.IsDisposed || !seen.Add(current))
            return;

        ReadOnlySpan<RenderNode> children = current.ChildNodes;
        for (int i = 0; i < children.Length; i++)
            WarmCaches(children[i], seen);

        current.Cache.ReportRenderCount(RenderNodeCache.Count);
        current.HasChanges = false;
    }

    private static Dictionary<RenderFragmentId, RenderFragmentReference> IndexReferences(
        RecordedRenderGraph graph)
    {
        var references = new Dictionary<RenderFragmentId, RenderFragmentReference>(graph.Fragments.Length);
        foreach (RecordedRenderFragment fragment in graph.Fragments)
        {
            if (fragment.Payload is RenderFragmentReference reference)
                references.Add(fragment.Id, reference);
        }

        return references;
    }

    private static GraphCase SharedFragmentIdCandidates()
    {
        RenderFragmentReference leaf = Pure();
        RenderFragmentReference middle = Pure([leaf]);
        RenderFragmentReference root = Pure([middle]);
        return BuildSynthetic(
            [leaf, middle, root],
            [root],
            [(leaf, new object()), (middle, new object()), (middle, new object()), (root, new object())]);
    }

    private static GraphCase DiamondInputs()
    {
        RenderFragmentReference shared = Pure();
        RenderFragmentReference left = Pure([shared]);
        RenderFragmentReference right = Pure([shared]);
        RenderFragmentReference root = Pure([left, right]);
        return BuildSynthetic(
            [shared, left, right, root],
            [root],
            [(shared, new object()), (left, new object()), (right, new object()), (root, new object())]);
    }

    private static GraphCase DeepChain()
    {
        var chain = new List<RenderFragmentReference>();
        RenderFragmentReference current = Pure();
        chain.Add(current);
        for (int index = 0; index < 39; index++)
        {
            current = Pure([current]);
            chain.Add(current);
        }

        return BuildSynthetic(
            chain,
            [current],
            [.. chain.Select(static reference => (reference, (object)new object()))]);
    }

    private static GraphCase DisconnectedRoots()
    {
        RenderFragmentReference leftLeaf = Pure();
        RenderFragmentReference leftRoot = Pure([leftLeaf]);
        RenderFragmentReference rightLeaf = Pure();
        RenderFragmentReference rightRoot = Pure([rightLeaf]);
        return BuildSynthetic(
            [leftLeaf, leftRoot, rightLeaf, rightRoot],
            [leftRoot, rightRoot],
            [
                (leftLeaf, new object()),
                (leftRoot, new object()),
                (rightLeaf, new object()),
                (rightRoot, new object()),
            ]);
    }

    private static GraphCase BuildSynthetic(
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlyList<(RenderFragmentReference Reference, object Key)> candidates)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            cachePolicy: RenderCacheOptions.Enabled,
            targetDomain: s_syntheticBounds));
        var builder = new RecordedRenderGraphBuilder(request.Id);
        foreach (RenderFragmentReference reference in references)
        {
            RenderProvenanceId provenanceId = builder.AddProvenance(reference, "topology-test");
            RenderValueId[] valueInputs = reference.Inputs.SelectMany(static item => item.ValueIds).ToArray();
            reference.ValueIds = [builder.AddValue(valueInputs, provenanceId, reference)];
            reference.Id = builder.AddFragment(reference.ValueIds, provenanceId, reference);
        }

        foreach ((RenderFragmentReference reference, object key) in candidates)
            builder.AddCacheCandidate(reference.Id!.Value, key);
        foreach (RenderFragmentReference root in roots)
            builder.PublishRoot(root.Id!.Value);

        RecordedRenderGraph graph = builder.Build();
        return new GraphCase(graph, IndexReferences(graph), [request]);
    }

    private static RenderFragmentReference Pure(IReadOnlyList<RenderFragmentReference>? inputs = null)
        => new(
            RenderFragmentKind.ContributeValues,
            s_syntheticBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs ?? [],
            payload: null,
            static _ => true);
}

internal static class RenderCacheCandidateTopologyScenes
{
    public static Drawable.Resource[] CreateRepresentativeScene()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = 240 },
            Height = { CurrentValue = 160 },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return [background.ToResource(context), accent.ToResource(context), label.ToResource(context)];
    }

    public static Drawable.Resource[] CreateShapeScene(int shapes, PixelSize frameSize)
    {
        CompositionContext context = CompositionContext.Default;
        var result = new List<Drawable.Resource>(shapes + 1);
        var background = new RectShape
        {
            Width = { CurrentValue = frameSize.Width },
            Height = { CurrentValue = frameSize.Height },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };
        result.Add(background.ToResource(context));

        for (int index = 0; index < shapes; index++)
        {
            var shape = new EllipseShape
            {
                Width = { CurrentValue = 20 + index % 7 },
                Height = { CurrentValue = 20 + index % 5 },
                Fill = { CurrentValue = Brushes.OrangeRed },
                FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 50 + index % 40 } } },
                Transform = { CurrentValue = new TranslateTransform(index % 17 * 7, index % 11 * 9) },
            };
            result.Add(shape.ToResource(context));
        }

        return [.. result];
    }
}
