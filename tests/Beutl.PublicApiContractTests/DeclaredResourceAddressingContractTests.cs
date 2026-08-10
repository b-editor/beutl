using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class DeclaredResourceAddressingContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [TestCase(false)]
    [TestCase(true)]
    public void NamedBindingsRemainStableWhenSameTypedResourcesAreReordered(bool reverse)
    {
        var reached = new List<string>();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> left = context.Borrow(
                new Payload("left", reached),
                cacheKey: "left",
                version: 1);
            RenderResource<Payload> right = context.Borrow(
                new Payload("right", reached),
                cacheKey: "right",
                version: 1);
            RenderResourceBinding[] bindings = reverse
                ? [right.Bind("right"), left.Bind("left")]
                : [left.Bind("left"), right.Bind("right")];
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                static (session, _) => session.UseDeclaredResource<Payload>("left", leftPayload =>
                    session.UseDeclaredResource<Payload>("right", rightPayload =>
                    {
                        leftPayload.Touch();
                        rightPayload.Touch();
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                        session.Publish(output);
                    })),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: bindings)));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(reached, Is.EqualTo(new[] { "left", "right" }));
        });
    }

    [Test]
    public void BindingValidationRejectsBlankAndDuplicateNames()
    {
        ArgumentException? blank = null;
        ArgumentException? duplicate = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> first = context.Borrow(new Payload(), cacheKey: "first");
            RenderResource<Payload> second = context.Borrow(new Payload(), cacheKey: "second");
            blank = Assert.Throws<ArgumentException>(() => first.Bind(" "));
            duplicate = Assert.Throws<ArgumentException>(() => OpaqueRenderDescription.Create(
                s_bounds,
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [first.Bind("payload"), second.Bind("payload")]));
        });

        _ = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(blank!.ParamName, Is.EqualTo("name"));
            Assert.That(duplicate!.ParamName, Is.EqualTo("resources"));
        });
    }

    [TestCase(ResourceFailure.MissingName, typeof(KeyNotFoundException), "missing")]
    [TestCase(ResourceFailure.TypeMismatch, typeof(InvalidOperationException), "OtherPayload")]
    public void NamedLookupFailsDeterministically(
        ResourceFailure failure,
        Type exceptionType,
        string messageFragment)
    {
        using var node = new LookupFailureNode(failure);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);

        Exception? exception = Assert.Throws(exceptionType, () => renderer.Rasterize());

        Assert.That(exception!.Message, Does.Contain(messageFragment));
    }

    [Test]
    public void PaintedSessionUsesTheAuthorBindingNamespace()
    {
        var reached = new List<string>();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(
                new Payload("painted", reached),
                cacheKey: "painted");
            context.Publish(context.PaintedSource(
                state: s_bounds,
                draw: static (session, _) => session.UseDeclaredResource<Payload>("author", payload =>
                {
                    payload.Touch();
                    session.Canvas.DrawRectangle(s_bounds, session.Fill, session.Pen);
                }),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token.Bind("author")]));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, RenderCacheOptions.Disabled);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(reached, Is.EqualTo(new[] { "painted" }));
        });
    }

    [Test]
    public void BindingIdentityInvalidatesTheOutputCacheWhenTheVersionChanges()
    {
        using var node = new VersionedPayloadNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        using (RenderNodeRasterization second = renderer.Rasterize())
        {
            Assert.That(first.IsEmpty || second.IsEmpty, Is.False);
        }

        node.Version++;
        using RenderNodeRasterization third = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(third.IsEmpty, Is.False);
            Assert.That(node.ExecutionCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void DeepStateIdentityPreventsAuthorEqualityFromReusingStaleOutput()
    {
        using var node = new IncompleteEqualityStateNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            AssertPixel(first.Bitmap!, red: 1, blue: 0);
        }

        node.Color = Colors.Blue;
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.That(second.IsEmpty, Is.False);
        AssertPixel(second.Bitmap!, red: 0, blue: 1);
        Assert.That(node.ExecutionCount, Is.EqualTo(2),
            "the state omits Color from its author-provided equality, but the callback reads Color");
    }

    [Test]
    public void DeepStateIdentityComparesNestedFieldsAndKeepsEqualHashesConsistent()
    {
        using var node = new IncompleteEqualityStateNode { NestedRevision = 3 };
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            AssertPixel(first.Bitmap!, red: 1, blue: 0);
        }

        using (RenderNodeRasterization equal = renderer.Rasterize())
        {
            AssertPixel(equal.Bitmap!, red: 1, blue: 0);
        }

        node.NestedRevision = 4;
        using RenderNodeRasterization nestedChange = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            AssertPixel(nestedChange.Bitmap!, red: 1, blue: 0);
            Assert.That(node.ExecutionCount, Is.EqualTo(2),
                "the equal second snapshot must hit, while a nested field omitted by author equality must miss");
        });
    }

    [Test]
    public void PersistentIdentityRejectsLeaseBoundExecutionValues()
    {
        object[] invalidIdentities =
        [
            default(PaintedRenderCanvas),
            LoweredBrush.Empty,
            LoweredPen.Empty,
        ];

        Assert.Multiple(() =>
        {
            foreach (object invalidIdentity in invalidIdentities)
            {
                Assert.That(
                    () => RenderHitTestContract.Custom(
                        static (_, _) => false,
                        structuralKey: invalidIdentity),
                    Throws.TypeOf<ArgumentException>()
                        .With.Property("ParamName").EqualTo("structuralKey"),
                    invalidIdentity.GetType().Name);
            }

            Assert.That(
                () => OpaqueRenderDescription.Create(
                    default(PaintedRenderCanvas),
                    static (_, _) => { },
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("state"));
        });
    }

    [TestCaseSource(nameof(ExactTerminalRepresentationCases))]
    public void DeepStateIdentityPreservesCallbackObservableTerminalRepresentations(
        object firstValue,
        object secondValue)
    {
        var firstState = (ExactTerminalState)firstValue;
        var secondState = (ExactTerminalState)secondValue;
        using var node = new ExactTerminalStateNode(firstState);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            AssertPixel(first.Bitmap!, red: 1, blue: 0);
        }

        node.State = firstState.Clone();
        using (RenderNodeRasterization equal = renderer.Rasterize())
        {
            AssertPixel(equal.Bitmap!, red: 1, blue: 0);
        }

        node.State = secondState;
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            AssertPixel(second.Bitmap!, red: 0, blue: 1);
            Assert.That(node.ExecutionCount, Is.EqualTo(2),
                "an equal clone must hit, while a callback-observable representation change must miss");
        });
    }

    [Test]
    public void CallbackRuntimeIdentity_IsNotAPublicAuthoringChannel()
    {
        Type[] descriptions =
        [
            typeof(OpaqueRenderDescription),
            typeof(GeometryDescription),
            typeof(TargetScopeDescription),
            typeof(TargetCommandDescription),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(OpaqueRenderDescription).Assembly.GetExportedTypes()
                    .Any(static type => type.FullName == "Beutl.Graphics.Rendering.RenderRuntimeIdentity"),
                Is.False);
            foreach (Type description in descriptions)
            {
                Assert.That(
                    description.GetProperty("RuntimeIdentity", BindingFlags.Public | BindingFlags.Instance),
                    Is.Null,
                    description.FullName);
            }
        });
    }

    [TestCaseSource(nameof(ExactTerminalRepresentationCases))]
    public void ExactTerminalStateIdentityPreventsStaleOutputReuse(
        object firstValue,
        object secondValue)
    {
        var firstState = (ExactTerminalState)firstValue;
        var secondState = (ExactTerminalState)secondValue;
        using var node = new ExactTerminalStateNode(firstState);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Enabled);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            AssertPixel(first.Bitmap!, red: 1, blue: 0);
        }

        node.State = secondState;
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.That(second.IsEmpty, Is.False);
        AssertPixel(second.Bitmap!, red: 0, blue: 1);
        Assert.That(node.ExecutionCount, Is.EqualTo(2),
            "Changing an observable terminal representation must invalidate the output cache.");
    }

    [Test]
    public void MutableTypeDelegatorStateIsRejectedBeforeItsDelegatedTypeCanChange()
    {
        var mutableType = new MutableTypeDelegator(typeof(string));

        Assert.That(
            () => CreateStateDescription(new ExactTerminalState(
                TerminalRepresentation.TypeIdentity,
                type: mutableType)),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));

        mutableType.Retarget(typeof(int));
        Assert.That(
            () => CreateStateDescription(new ExactTerminalState(
                TerminalRepresentation.TypeIdentity,
                type: mutableType)),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
    }

    [Test]
    public void NativePointerSizedStateIsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateStateDescription((IntPtr)1),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
            Assert.That(
                () => CreateStateDescription((UIntPtr)1),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
        });
    }

    [Test]
    public void RecursiveStateTypeGraphsAreRejected()
    {
        var state = new RecursiveState(null);

        Assert.That(
            () => CreateStateDescription(state),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
    }

    [Test]
    public void MutableStateIsRejectedUnlessTheDescriptionIsRequestLocal()
    {
        var state = new MutableState();
        Assert.Multiple(() =>
        {
            Assert.That(
                () => OpaqueRenderDescription.Create(
                    state,
                    static (_, _) => { },
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("state"));
            Assert.That(
                () => OpaqueRenderDescription.CreateRequestLocal(
                    _ => state.Value++,
                    OpaqueRenderBoundsContract.Source(s_bounds),
                    RenderHitTestContract.None,
                    RenderValueCardinality.Single,
                    RenderScaleContract.Vector),
                Throws.Nothing);
        });
    }

    [Test]
    public void EveryDeclaredResourceSessionUsesAStringNameAndRawSessionsRemainTokenOnly()
    {
        Type[] namedSessions =
        [
            typeof(OpaqueRenderSession),
            typeof(GeometrySession),
            typeof(TargetScopeSession),
            typeof(TargetCommandSession),
            typeof(PaintedRenderSession),
        ];
        foreach (Type session in namedSessions)
        {
            MethodInfo? method = session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(static candidate => candidate.Name == "UseDeclaredResource");
            Assert.That(method, Is.Not.Null, session.Name);
            Assert.That(method!.GetParameters()[0].ParameterType, Is.EqualTo(typeof(string)), session.Name);
        }

        Assert.Multiple(() =>
        {
            Assert.That(typeof(RenderResourceBinding).GetConstructors(), Is.Empty);
            Assert.That(typeof(RawTargetScopeSession).GetMethod("UseDeclaredResource"), Is.Null);
            Assert.That(typeof(RawTargetCommandSession).GetMethod("UseDeclaredResource"), Is.Null);
            Assert.That(typeof(RawTargetScopeSession).GetMethod("UseResource"), Is.Not.Null);
            Assert.That(typeof(RawTargetCommandSession).GetMethod("UseResource"), Is.Not.Null);
        });
    }

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, RenderCacheOptions.Disabled);
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node, RenderCacheOptions cacheOptions)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, cacheOptions);
        return renderer.Rasterize();
    }

    private static OpaqueRenderDescription CreateStateDescription(IncompleteEqualityState state)
        => OpaqueRenderDescription.Create(
            state,
            static (session, current) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(current.Bounds);
                output.Canvas.Use(canvas => canvas.Clear(current.Color));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    private static OpaqueRenderDescription CreateStateDescription(ExactTerminalState state)
        => OpaqueRenderDescription.Create(
            state,
            static (_, _) => { },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.None,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

    private static OpaqueRenderDescription CreateStateDescription(RecursiveState state)
        => OpaqueRenderDescription.Create(
            state,
            static (_, _) => { },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.None,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

    private static OpaqueRenderDescription CreateStateDescription<TState>(TState state)
        where TState : notnull
        => OpaqueRenderDescription.Create(
            state,
            static (_, _) => { },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.None,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

    private static void AssertPixel(Bitmap bitmap, float red, float blue)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        Span<Half> pixel = bitmap.GetRow<Half>(bitmap.Height / 2)
            .Slice((bitmap.Width / 2) * 4, 4);
        float actualRed = (float)pixel[0];
        float actualBlue = (float)pixel[2];
        float actualAlpha = (float)pixel[3];
        Assert.Multiple(() =>
        {
            Assert.That(actualRed, Is.EqualTo(red).Within(0.01f));
            Assert.That(actualBlue, Is.EqualTo(blue).Within(0.01f));
            Assert.That(actualAlpha, Is.EqualTo(1).Within(0.01f));
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, RenderCacheOptions cacheOptions)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = cacheOptions,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    public enum ResourceFailure
    {
        MissingName,
        TypeMismatch,
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class LookupFailureNode(ResourceFailure failure) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                failure,
                static (session, currentFailure) =>
                {
                    if (currentFailure == ResourceFailure.MissingName)
                    {
                        session.UseDeclaredResource<Payload>("missing", static _ => { });
                    }
                    else
                    {
                        session.UseDeclaredResource<OtherPayload>("payload", static _ => { });
                    }
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [token.Bind("payload")])));
        }
    }

    private sealed class VersionedPayloadNode : RenderNode
    {
        private readonly Payload _payload = new();

        public int Version { get; set; } = 1;

        public int ExecutionCount => _payload.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> token = context.Borrow(
                _payload,
                cacheKey: "versioned-payload",
                version: Version);
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                static (session, _) => session.UseDeclaredResource<Payload>("payload", payload =>
                {
                    payload.Touch();
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.Red));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [token.Bind("payload")])));
        }
    }

    private sealed class IncompleteEqualityStateNode : RenderNode
    {
        private readonly Payload _probe = new();

        public Color Color { get; set; } = Colors.Red;

        public int NestedRevision { get; set; } = 1;

        public int ExecutionCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> probe = context.Borrow(
                _probe,
                cacheKey: typeof(IncompleteEqualityStateNode));
            var state = new IncompleteEqualityState(
                s_bounds,
                Color,
                new NestedState(NestedRevision, new NestedLeaf(2)));
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                state,
                static (session, current) =>
                    session.UseDeclaredResource<Payload>("probe", probeValue =>
                    {
                        probeValue.Touch();
                        using OpaqueRenderOutput output = session.CreateOutput(current.Bounds);
                        output.Canvas.Use(canvas => canvas.Clear(current.Color));
                        session.Publish(output);
                    }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [probe.Bind("probe")])));
        }
    }

    private sealed class IncompleteEqualityState(
        Rect bounds,
        Color color,
        NestedState nested)
    {
        public readonly Rect Bounds = bounds;
        public readonly Color Color = color;
        public readonly NestedState Nested = nested;

        public override bool Equals(object? obj)
            => obj is IncompleteEqualityState other && Bounds == other.Bounds;

        public override int GetHashCode() => Bounds.GetHashCode();
    }

    private static IEnumerable<TestCaseData> ExactTerminalRepresentationCases()
    {
        yield return new TestCaseData(
                new ExactTerminalState(TerminalRepresentation.SignedZero, single: 0f),
                new ExactTerminalState(TerminalRepresentation.SignedZero, single: -0f))
            .SetName("{m}(SignedZero)");
        yield return new TestCaseData(
                new ExactTerminalState(
                    TerminalRepresentation.NaNPayload,
                    single: BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001))),
                new ExactTerminalState(
                    TerminalRepresentation.NaNPayload,
                    single: BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002))))
            .SetName("{m}(NaNPayload)");
        yield return new TestCaseData(
                new ExactTerminalState(
                    TerminalRepresentation.DecimalScale,
                    decimalValue: new decimal(1, 0, 0, isNegative: false, scale: 0)),
                new ExactTerminalState(
                    TerminalRepresentation.DecimalScale,
                    decimalValue: new decimal(10, 0, 0, isNegative: false, scale: 1)))
            .SetName("{m}(DecimalScale)");

        long ticks = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Unspecified).Ticks;
        yield return new TestCaseData(
                new ExactTerminalState(
                    TerminalRepresentation.DateTimeKind,
                    dateTime: new DateTime(ticks, DateTimeKind.Unspecified)),
                new ExactTerminalState(
                    TerminalRepresentation.DateTimeKind,
                    dateTime: new DateTime(ticks, DateTimeKind.Utc)))
            .SetName("{m}(DateTimeKind)");

        var utc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        yield return new TestCaseData(
                new ExactTerminalState(TerminalRepresentation.DateTimeOffset, dateTimeOffset: utc),
                new ExactTerminalState(
                    TerminalRepresentation.DateTimeOffset,
                    dateTimeOffset: utc.ToOffset(TimeSpan.FromHours(9))))
            .SetName("{m}(DateTimeOffset)");
        yield return new TestCaseData(
                new ExactTerminalState(
                    TerminalRepresentation.TypeIdentity,
                    type: typeof(string)),
                new ExactTerminalState(
                    TerminalRepresentation.TypeIdentity,
                    type: typeof(int)))
            .SetName("{m}(TypeIdentity)");
    }

    private sealed class ExactTerminalState(
        TerminalRepresentation representation,
        float single = 0,
        decimal decimalValue = 0,
        DateTime dateTime = default,
        DateTimeOffset dateTimeOffset = default,
        Type? type = null)
    {
        public readonly TerminalRepresentation Representation = representation;
        public readonly float Single = single;
        public readonly decimal Decimal = decimalValue;
        public readonly DateTime DateTime = dateTime;
        public readonly DateTimeOffset DateTimeOffset = dateTimeOffset;
        public readonly Type? Type = type;

        public ExactTerminalState Clone()
            => new(Representation, Single, Decimal, DateTime, DateTimeOffset, Type);

        public bool IsSecondRepresentation()
            => Representation switch
            {
                TerminalRepresentation.SignedZero => BitConverter.SingleToInt32Bits(Single) < 0,
                TerminalRepresentation.NaNPayload => BitConverter.SingleToInt32Bits(Single)
                                                    == unchecked((int)0x7fc00002),
                TerminalRepresentation.DecimalScale => (decimal.GetBits(Decimal)[3] >> 16 & 0x7f) == 1,
                TerminalRepresentation.DateTimeKind => DateTime.Kind == DateTimeKind.Utc,
                TerminalRepresentation.DateTimeOffset => DateTimeOffset.Offset != TimeSpan.Zero,
                TerminalRepresentation.TypeIdentity => Type!.UnderlyingSystemType == typeof(int),
                _ => throw new InvalidOperationException("The terminal representation is invalid."),
            };
    }

    private enum TerminalRepresentation : byte
    {
        SignedZero,
        NaNPayload,
        DecimalScale,
        DateTimeKind,
        DateTimeOffset,
        TypeIdentity,
    }

    private sealed class MutableTypeDelegator(Type delegatedType) : TypeDelegator(delegatedType)
    {
        public void Retarget(Type type) => typeImpl = type;
    }

    private sealed class ExactTerminalStateNode(ExactTerminalState state) : RenderNode
    {
        private readonly Payload _probe = new();

        public ExactTerminalState State { get; set; } = state;

        public int ExecutionCount => _probe.Count;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> probe = context.Borrow(
                _probe,
                cacheKey: typeof(ExactTerminalStateNode));
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                State,
                static (session, current) =>
                    session.UseDeclaredResource<Payload>("probe", probeValue =>
                    {
                        probeValue.Touch();
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.Clear(
                            current.IsSecondRepresentation() ? Colors.Blue : Colors.Red));
                        session.Publish(output);
                    }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [probe.Bind("probe")])));
        }
    }

    private readonly record struct NestedState(int Revision, NestedLeaf Leaf);

    private readonly record struct NestedLeaf(int Value);

    private sealed class RecursiveState(RecursiveState? next)
    {
        public readonly RecursiveState? Next = next;
    }

    private sealed class Payload(string? name = null, List<string>? reached = null)
    {
        public int Count { get; private set; }

        public void Touch()
        {
            Count++;
            if (name is not null)
                reached!.Add(name);
        }
    }

    private sealed class OtherPayload;

    private sealed class MutableState
    {
        public int Value { get; set; }
    }
}
