using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class NodeRecordingTransactionTests
{
    [Test]
    public void Commit_PublishesFragmentsResourcesAndCachePolicyAtomically()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentHandle first = CreateSource(transaction, new Rect(0, 0, 10, 10));
        RenderFragmentHandle second = CreateSource(transaction, new Rect(10, 0, 10, 10));
        var resource = new TrackedDisposable("owned");
        RenderResource<TrackedDisposable> token = transaction.Own(resource);

        transaction.Publish(first);
        transaction.Publish(second);
        transaction.DisableRenderCache();

        Assert.Multiple(() =>
        {
            Assert.That(host.Commits, Is.Empty, "A checkpoint must not leak partial graph state before commit.");
            Assert.That(host.IsRenderCacheEnabled, Is.True);
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Pending));
        });

        IReadOnlyList<RenderFragmentReference> publications = transaction.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(host.Commits, Has.Count.EqualTo(1));
            Assert.That(host.Commits[0].Fragments, Has.Length.EqualTo(2));
            Assert.That(host.Commits[0].Publications, Has.Length.EqualTo(2));
            Assert.That(publications, Is.EqualTo(host.Commits[0].Publications));
            Assert.That(host.IsRenderCacheEnabled, Is.True,
                "A node opting out of the cache must not move the request-wide policy.");
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Committed));
            Assert.That(resource.DisposeCount, Is.Zero, "A committed owned resource belongs to the request.");
        });

        owner.Cleanup();
        Assert.That(resource.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Commit_IgnoresUnpublishedFragmentsDuringFanOutValidation()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        var context = new RenderNodeContext(transaction);
        var publishedBounds = new Rect(0, 0, 10, 10);
        RenderFragmentHandle published = CreateSource(transaction, publishedBounds);
        RenderFragmentHandle discarded = context.Blend(published, BlendMode.SrcOver);
        _ = context.Opacity(discarded, 0.25f);
        _ = context.Opacity(discarded, 0.75f);
        transaction.Publish(published);

        Assert.That(() => transaction.Commit(), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(host.Commits, Has.Count.EqualTo(1));
            Assert.That(host.Commits[0].Fragments, Has.Length.EqualTo(4),
                "Committed but unpublished fragments remain available for skipped-outcome reconciliation.");
            Assert.That(host.Commits[0].Fragments[0].Reference.Bounds, Is.EqualTo(publishedBounds));
        });
    }


    // Recording allocates one opacity fragment per drawable per pass. The SkSL text is a compile-time constant,
    // so a pass must neither re-tokenize it nor mint a description per fragment.
    [Test]
    public void Opacity_ReusesOneValidatedFusionDescriptionPerNormalizedValue()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        var context = new RenderNodeContext(transaction);
        var bounds = new Rect(0, 0, 10, 10);
        RenderFragmentHandle source = CreateSource(transaction, bounds);

        for (int i = 0; i < 8; i++)
            transaction.Publish(context.Opacity(source, 0.375f));

        transaction.Publish(context.Opacity(source, 0.75f));
        transaction.Publish(context.Opacity(source, 4f));
        transaction.Publish(context.Opacity(source, 1f));
        transaction.Commit();

        ShaderDescription[] descriptions = host.Commits[0].Fragments
            .Select(static entry => entry.Reference.Payload)
            .OfType<OpacityRenderFragmentPayload>()
            .Select(static payload => payload.FusionDescription)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(descriptions, Has.Length.EqualTo(11));
            Assert.That(
                descriptions,
                Has.All.Matches<ShaderDescription>(
                    item => ReferenceEquals(item.Source, descriptions[0].Source)),
                "The constant fusion source must be normalized and validated once.");
            Assert.That(
                descriptions.Take(8),
                Has.All.SameAs(descriptions[0]),
                "Fragments sharing one opacity must share one immutable description.");
            Assert.That(descriptions[8], Is.Not.SameAs(descriptions[0]));
            Assert.That(
                descriptions[9],
                Is.SameAs(descriptions[10]),
                "An out-of-range opacity clamps onto the description of its normalized value.");
            Assert.That(
                descriptions[0],
                Is.SameAs(OpacityRenderNode.CreateFusionDescription(0.375f)));
        });
    }

    [Test]
    public void Rollback_DiscardsEveryPartialEffectAndRestoresCacheDisablement()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentHandle fragment = CreateSource(transaction, new Rect(0, 0, 10, 10));
        var resource = new TrackedDisposable("owned");
        RenderResource<TrackedDisposable> token = transaction.Own(resource);
        var primary = new InvalidOperationException("process failed");

        transaction.Publish(fragment);
        transaction.DisableRenderCache();

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() => transaction.Rollback(primary));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(primary));
            Assert.That(transaction.State, Is.EqualTo(NodeRecordingTransactionState.RolledBack));
            Assert.That(host.Commits, Is.Empty);
            Assert.That(host.IsRenderCacheEnabled, Is.True,
                "A rolled-back cache disablement must not escape its checkpoint.");
            Assert.That(token.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
        });
    }

    [Test]
    public void DisableRenderCache_DoesNotEscapeTheCheckpointThatRequestedIt()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request) { ChildAction = static context => context.DisableRenderCache() };
        var parent = new NodeRecordingTransaction(host, new object(), []);
        using var childNode = new MemoryNode<int>(0);

        _ = parent.RecordNode(childNode, [], subtree: false);

        Assert.Multiple(() =>
        {
            Assert.That(parent.IsRenderCacheEnabled, Is.True,
                "A committed child must not decide the cache policy for the checkpoint that recorded it.");
            Assert.That(host.IsRenderCacheEnabled, Is.True);
        });

        parent.Commit();

        Assert.That(host.IsRenderCacheEnabled, Is.True,
            "Committing a checkpoint must leave the request-wide policy where it started.");
    }

    [Test]
    public void DisableRenderCache_ReachesTheNodesRecordedInsideIt()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        bool? childSawCacheEnabled = null;
        var host = new RecordingHost(request)
        {
            ChildAction = context => childSawCacheEnabled = context.IsRenderCacheEnabled,
        };
        var parent = new NodeRecordingTransaction(host, new object(), []);
        using var childNode = new MemoryNode<int>(0);

        parent.DisableRenderCache();
        _ = parent.RecordNode(childNode, [], subtree: false);

        Assert.That(childSawCacheEnabled, Is.False,
            "A node recorded inside a disabled checkpoint is part of the output that must not be cached.");
    }

    [Test]
    public void NestedRecording_UsesFreshChildAndParentFacadeHandles()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request)
        {
            ChildAction = static context => context.PassThrough(),
        };
        var parent = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentHandle parentInput = CreateSource(parent, new Rect(1, 2, 30, 40));
        using var childNode = new MemoryNode<int>(0);

        IReadOnlyList<RenderFragmentHandle> mapped = parent.RecordNode(
            childNode,
            [parentInput],
            subtree: false);

        RenderFragmentHandle childFacade = host.LastChildInputs.Single();
        RenderFragmentHandle parentFacade = mapped.Single();
        Assert.Multiple(() =>
        {
            Assert.That(childFacade, Is.Not.SameAs(parentInput));
            Assert.That(parentFacade, Is.Not.SameAs(parentInput));
            Assert.That(parentFacade, Is.Not.SameAs(childFacade));
            Assert.That(parentFacade.TryGetMetadata(out RenderFragmentMetadata parentFacadeMetadata), Is.True);
            Assert.That(parentInput.TryGetMetadata(out RenderFragmentMetadata parentInputMetadata), Is.True);
            Assert.That(parentFacadeMetadata, Is.EqualTo(parentInputMetadata));
            Assert.That(() => childFacade.TryGetMetadata(out _), Throws.TypeOf<InvalidOperationException>(),
                "Child facades must seal when the child checkpoint ends.");
            Assert.That(() => parentInput.TryGetMetadata(out _), Throws.Nothing,
                "The original parent handle remains active.");
        });
    }

    [Test]
    public void NestedRequest_IsStagedUntilCommitAndDisposedOnRollback()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        using var root = new MemoryNode<int>(0);
        using var committedBinding = new NestedRenderTargetBinding();

        var committedTransaction = new NodeRecordingTransaction(host, new object(), []);
        RecordedNestedRenderRequest committedNested = committedTransaction.RecordNestedRequest(
            root,
            request.Options.CreateNested(committedBinding));

        Assert.That(host.Commits, Is.Empty,
            "A nested graph must remain checkpoint-local before the parent commits.");

        committedTransaction.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(host.Commits, Has.Count.EqualTo(1));
            Assert.That(host.Commits[0].NestedRequests, Has.Length.EqualTo(1));
            Assert.That(host.Commits[0].NestedRequests[0], Is.SameAs(committedNested));
            Assert.That(committedNested.Request.State, Is.Not.EqualTo(RenderRequestState.Disposed));
        });
        committedNested.Request.Dispose();

        using var rolledBackBinding = new NestedRenderTargetBinding();
        var rolledBackTransaction = new NodeRecordingTransaction(host, new object(), []);
        RecordedNestedRenderRequest rolledBackNested = rolledBackTransaction.RecordNestedRequest(
            root,
            request.Options.CreateNested(rolledBackBinding));
        var primary = new InvalidOperationException("rollback nested request");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => rolledBackTransaction.Rollback(primary));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(host.Commits, Has.Count.EqualTo(1),
                "Rollback must not publish the staged nested graph.");
            Assert.That(rolledBackNested.Request.State, Is.EqualTo(RenderRequestState.Disposed));
        });
    }

    [Test]
    public void CommitAndRollback_RejectRetainedContextsAndHandles()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);

        var committedTransaction = new NodeRecordingTransaction(host, new object(), []);
        var committedContext = new RenderNodeContext(committedTransaction);
        RenderFragmentHandle committedHandle = CreateSource(committedTransaction, new Rect(0, 0, 1, 1));
        committedTransaction.Publish(committedHandle);
        committedTransaction.Commit();

        var rolledBackTransaction = new NodeRecordingTransaction(host, new object(), []);
        var rolledBackContext = new RenderNodeContext(rolledBackTransaction);
        RenderFragmentHandle rolledBackHandle = CreateSource(rolledBackTransaction, new Rect(0, 0, 1, 1));
        var primary = new InvalidOperationException("rollback");
        InvalidOperationException? rollbackFailure = Assert.Throws<InvalidOperationException>(
            () => rolledBackTransaction.Rollback(primary));
        Assert.That(rollbackFailure, Is.SameAs(primary));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => committedHandle.TryGetMetadata(out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.Inputs, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.Intent, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.Purpose, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.OutputScale, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.MaxWorkingScale, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = committedContext.IsRenderCacheEnabled, Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => committedContext.TryCalculateInputBounds(out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => committedContext.DisableRenderCache(), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => rolledBackHandle.TryGetMetadata(out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = rolledBackContext.Inputs, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => rolledBackContext.DisableRenderCache(), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void Rollback_ReleasesOwnedResourcesInReverseOrderAndPreservesPrimaryFailure()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        var disposalOrder = new List<string>();
        var first = new TrackedDisposable("first", disposalOrder);
        var secondCleanupFailure = new InvalidOperationException("second cleanup failed");
        var second = new TrackedDisposable("second", disposalOrder, secondCleanupFailure);
        var third = new TrackedDisposable("third", disposalOrder);
        var primary = new InvalidOperationException("recording failed");

        transaction.Own(first);
        transaction.Own(second);
        transaction.Own(third);

        InvalidOperationException? rollbackFailure = Assert.Throws<InvalidOperationException>(
            () => transaction.Rollback(primary));
        Assert.That(rollbackFailure, Is.SameAs(primary));

        Assert.Multiple(() =>
        {
            Assert.That(disposalOrder, Is.EqualTo(new[] { "third", "second", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
            Assert.That(owner.PrimaryFailure?.SourceException, Is.SameAs(primary));
            Assert.That(owner.SecondaryFailures, Has.Length.EqualTo(1));
            Assert.That(owner.SecondaryFailures[0], Is.SameAs(secondCleanupFailure));
            Assert.That(owner.CleanupFailures, Is.EqualTo(new[] { secondCleanupFailure }));
        });

        owner.Cleanup();
        Assert.Multiple(() =>
        {
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void RollbackResources_ContinuesAfterCleanupFailureAndReportsAllFailures()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var transaction = new NodeRecordingTransaction(new RecordingHost(request), new object(), []);
        var disposalOrder = new List<string>();
        var first = new TrackedDisposable("first", disposalOrder);
        var secondFailure = new InvalidOperationException("second failed");
        var second = new TrackedDisposable("second", disposalOrder, secondFailure);
        var third = new TrackedDisposable("third", disposalOrder);
        RenderResource<TrackedDisposable> firstToken = transaction.Own(first);
        RenderResource<TrackedDisposable> secondToken = transaction.Own(second);
        RenderResource<TrackedDisposable> thirdToken = transaction.Own(third);

        AggregateException? failure = Assert.Throws<AggregateException>(
            () => transaction.RollbackResources([firstToken, secondToken, thirdToken]));

        Assert.Multiple(() =>
        {
            Assert.That(disposalOrder, Is.EqualTo(new[] { "third", "second", "first" }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
            Assert.That(third.DisposeCount, Is.EqualTo(1));
            Assert.That(failure!.InnerExceptions, Is.EqualTo(new[] { secondFailure }));
            Assert.That(firstToken.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(secondToken.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
            Assert.That(thirdToken.RegistrationState, Is.EqualTo(RenderResourceRegistrationState.Released));
        });
    }

    [Test]
    public void Recorder_RejectsDirectAndIndirectSubtreeCyclesWithAPath()
    {
        var direct = new ContainerRenderNode();
        direct.AddChild(direct);
        try
        {
            using var directOwner = new RenderRequestOwner();
            using var directRequest = CreateRequest(directOwner);
            var directRecorder = new RenderRequestRecorder(directRequest);

            InvalidOperationException? directFailure = Assert.Throws<InvalidOperationException>(
                () => directRecorder.Record(direct));

            Assert.That(directFailure!.Message, Does.Contain(nameof(ContainerRenderNode)));
            Assert.That(directFailure.Message, Does.Contain("->"));
        }
        finally
        {
            direct.RemoveChild(direct);
            direct.Dispose();
        }

        var first = new ContainerRenderNode();
        var second = new ContainerRenderNode();
        first.AddChild(second);
        second.AddChild(first);
        try
        {
            using var indirectOwner = new RenderRequestOwner();
            using var indirectRequest = CreateRequest(indirectOwner);
            var indirectRecorder = new RenderRequestRecorder(indirectRequest);

            InvalidOperationException? indirectFailure = Assert.Throws<InvalidOperationException>(
                () => indirectRecorder.Record(first));

            Assert.That(indirectFailure!.Message, Does.Contain("->"));
            Assert.That(indirectFailure.Message.Split(nameof(ContainerRenderNode)).Length - 1,
                Is.GreaterThanOrEqualTo(3));
        }
        finally
        {
            second.RemoveChild(first);
            first.RemoveChild(second);
            first.Dispose();
            second.Dispose();
        }
    }

    [Test]
    public void Recorder_RejectsDirectAndIndirectRecordNodeCyclesWithAPath()
    {
        using var directOwner = new RenderRequestOwner();
        using var directRequest = CreateRequest(directOwner);
        using var direct = new MemoryNode<int>(0);
        var directRecorder = new RenderRequestRecorder(directRequest);
        var directTransaction = new NodeRecordingTransaction(directRecorder, new object(), []);

        InvalidOperationException? directFailure;
        using (directOwner.RecordingFamily.Enter(direct))
        {
            directFailure = Assert.Throws<InvalidOperationException>(
                () => directTransaction.RecordNode(direct, [], subtree: false));
        }

        using var indirectOwner = new RenderRequestOwner();
        using var indirectRequest = CreateRequest(indirectOwner);
        using var first = new MemoryNode<int>(1);
        using var second = new MemoryNode<int>(2);
        var indirectRecorder = new RenderRequestRecorder(indirectRequest);
        var indirectTransaction = new NodeRecordingTransaction(indirectRecorder, new object(), []);

        InvalidOperationException? indirectFailure;
        using (indirectOwner.RecordingFamily.Enter(first))
        using (indirectOwner.RecordingFamily.Enter(second))
        {
            indirectFailure = Assert.Throws<InvalidOperationException>(
                () => indirectTransaction.RecordNode(first, [], subtree: false));
        }

        Assert.Multiple(() =>
        {
            Assert.That(directFailure!.Message, Does.Contain(nameof(MemoryNode<int>)));
            Assert.That(directFailure.Message, Does.Contain("->"));
            Assert.That(indirectFailure!.Message, Does.Contain(nameof(MemoryNode<int>)));
            Assert.That(indirectFailure.Message, Does.Contain("->"));
        });
    }

    [Test]
    public void Recorder_RejectsASeparateTargetCycleUsingTheRequestFamilyGuard()
    {
        using var owner = new RenderRequestOwner();
        RenderRequestOptions options = CreateOptions(owner);
        using var request = new RenderRequest(options);
        using var node = new MemoryNode<int>(0);
        using var binding = new NestedRenderTargetBinding();
        var recorder = new RenderRequestRecorder(request);

        InvalidOperationException? failure;
        using (owner.RecordingFamily.Enter(node))
        {
            failure = Assert.Throws<InvalidOperationException>(
                () => recorder.RecordNestedRequest(node, options.CreateNested(binding)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(nameof(MemoryNode<int>)));
            Assert.That(failure.Message, Does.Contain("->"));
        });
    }

    [Test]
    public void Recorder_AllowsSequentialReuseAfterTheActiveScopeEnds()
    {
        var root = new ContainerRenderNode();
        var repeated = new MemoryNode<int>(0);
        root.AddChild(repeated);
        root.AddChild(repeated);
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var recorder = new RenderRequestRecorder(request);

        Assert.That(() => recorder.Record(root), Throws.Nothing);

        root.RemoveChild(repeated);
        root.RemoveChild(repeated);
        root.Dispose();
        repeated.Dispose();
    }

    // The recording invariants are the diagnostic plugin authors get for a mistake that would otherwise
    // render nothing, so they run in every configuration and are asserted here without a DEBUG guard.
    [Test]
    public void Commit_RejectsAFanOutRestrictedFragmentPublishedTwice()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentHandle command = CreateTargetCommand(transaction, new Rect(0, 0, 10, 10), []);

        transaction.Publish(command);
        transaction.Publish(command);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => transaction.Commit(),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "A target-effect render fragment cannot be consumed or published more than once."));
            Assert.That(host.Commits, Is.Empty);
        });
    }

    [Test]
    public void Commit_RejectsAFanOutRestrictedFragmentConsumedByTwoPublishedFragments()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var transaction = new NodeRecordingTransaction(host, new object(), []);
        var bounds = new Rect(0, 0, 10, 10);
        RenderFragmentHandle shared = CreateTargetCommand(transaction, bounds, []);
        RenderFragmentReference sharedReference = transaction.GetReference(shared);

        transaction.Publish(CreateLayer(transaction, bounds, [sharedReference]));
        transaction.Publish(CreateLayer(transaction, bounds, [sharedReference]));

        Assert.That(
            () => transaction.Commit(),
            Throws.InvalidOperationException.With.Message.EqualTo(
                "A target-effect render fragment cannot be consumed or published more than once."));
    }

    // The orphan sweep is skipped when no entry shares this transaction's origin, so absorbing one has to
    // arm it. A stuck-off flag would drop the diagnostic silently, which is the one outcome that must not
    // happen.
    [Test]
    public void Commit_ReportsAnOrphanedTargetEffectAbsorbedFromAChildSharingItsOrigin()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var origin = new SharedRecordingOrigin();
        var bounds = new Rect(0, 0, 10, 10);
        var parent = new NodeRecordingTransaction(host, origin, []);
        var child = new NodeRecordingTransaction(host, origin, [], parent);
        RenderFragmentHandle orphan = CreateTargetCommand(child, bounds, []);

        child.Publish(orphan);
        child.Commit();
        transactionPublishesSomethingElse(parent, bounds);

        Assert.That(
            () => parent.Commit(),
            Throws.InvalidOperationException.With.Message
                .StartsWith("A recorded target-effect fragment was neither published nor consumed.")
                .And.Message.Contains($"Fragment kind: {RenderFragmentKind.TargetCommand}")
                .And.Message.Contains($"recorded by: {typeof(SharedRecordingOrigin).FullName}"));

        static void transactionPublishesSomethingElse(NodeRecordingTransaction transaction, Rect bounds)
            => transaction.Publish(CreateSource(transaction, bounds));
    }

    // A sealed transaction hands its ownership set back for reuse. A set that came back still holding its
    // last tenant's fragments would let the next transaction accept a foreign handle.
    [Test]
    public void Commit_ReleasesTheOwnershipSetWithoutLeakingItsFragmentsIntoTheNextTransaction()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        var host = new RecordingHost(request);
        var bounds = new Rect(0, 0, 10, 10);

        var sealedTransaction = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentHandle staleHandle = CreateSource(sealedTransaction, bounds);
        RenderFragmentReference stale = sealedTransaction.GetReference(staleHandle);
        sealedTransaction.Publish(staleHandle);
        sealedTransaction.Commit();

        var reusing = new NodeRecordingTransaction(host, new object(), []);
        RenderFragmentReference own = reusing.GetReference(CreateSource(reusing, bounds));

        Assert.Multiple(() =>
        {
            Assert.That(
                () => reusing.VerifyOwns(stale),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The render fragment belongs to a different recording transaction."),
                "A recycled ownership set must not vouch for its previous tenant's fragments.");
            Assert.That(() => reusing.VerifyOwns(own), Throws.Nothing);
            Assert.That(
                () => sealedTransaction.VerifyOwns(stale),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The render-node recording context and its fragment handles are no longer active."),
                "A sealed transaction answers ownership questions with its seal, not with a released set.");
        });
    }

    private static RenderFragmentHandle CreateTargetCommand(
        NodeRecordingTransaction transaction,
        Rect bounds,
        RenderFragmentReference[] inputs)
    {
        return transaction.CreateFragment(
            RenderFragmentKind.TargetCommand,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [.. inputs],
            payload: null,
            hitTest: RenderFragmentHitTest.Bounds);
    }

    private static RenderFragmentHandle CreateLayer(
        NodeRecordingTransaction transaction,
        Rect bounds,
        RenderFragmentReference[] inputs)
    {
        return transaction.CreateFragment(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [.. inputs],
            payload: null,
            hitTest: RenderFragmentHitTest.Bounds);
    }

    private sealed class SharedRecordingOrigin;

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
    {
        return new RenderRequest(CreateOptions(owner));
    }

    private static RenderRequestOptions CreateOptions(RenderRequestOwner owner)
    {
        return new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            owner: owner);
    }

    private static RenderFragmentHandle CreateSource(NodeRecordingTransaction transaction, Rect bounds)
    {
        return transaction.CreateFragment(
            RenderFragmentKind.OpaqueSource,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: [],
            payload: null,
            hitTest: RenderFragmentHitTest.Bounds);
    }

    private sealed class RecordingHost(RenderRequest request) : IRenderRequestRecordingHost
    {
        public RenderRequest Request { get; } = request;

        public bool IsRenderCacheEnabled { get; init; } = true;

        public Action<RenderNodeContext>? ChildAction { get; init; }

        public IReadOnlyList<RenderFragmentHandle> LastChildInputs { get; private set; } = [];

        public List<NodeRecordingCommit> Commits { get; } = [];

        public IReadOnlyList<RenderFragmentReference> RecordNode(
            NodeRecordingTransaction parent,
            RenderNode node,
            IReadOnlyList<RenderFragmentReference> inputs,
            bool subtree)
        {
            var child = new NodeRecordingTransaction(this, node, inputs, parent);
            LastChildInputs = child.Inputs;
            ChildAction?.Invoke(new RenderNodeContext(child));
            return child.Commit();
        }

        public RecordedNestedRenderRequest RecordNestedRequest(
            RenderNode root,
            RenderRequestOptions options)
        {
            var nestedRequest = new RenderRequest(options, Request);
            RecordedRenderGraph graph = new RecordedRenderGraphBuilder(nestedRequest.Id).Build();
            return new RecordedNestedRenderRequest(nestedRequest, graph);
        }

        public void Commit(in NodeRecordingCommit commit)
        {
            Commits.Add(commit);
            foreach (RenderResource resource in commit.Resources)
            {
                Request.Options.Owner.ResourceRegistry.Commit(resource);
            }

        }
    }

    private sealed class TrackedDisposable(
        string name,
        List<string>? disposalOrder = null,
        Exception? disposeFailure = null) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            disposalOrder?.Add(name);
            if (disposeFailure is not null)
                throw disposeFailure;
        }
    }

}
