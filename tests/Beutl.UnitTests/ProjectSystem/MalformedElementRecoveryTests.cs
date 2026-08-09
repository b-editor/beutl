using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

public sealed class MalformedElementRecoveryTests
{
    private string _root = null!;

    private sealed class IOExceptionElement : Element
    {
        public IOExceptionElement()
        {
            throw new IOException("Constructor could not access its storage.");
        }
    }

    private sealed class MissingAssemblyElement : Element
    {
        public MissingAssemblyElement()
        {
            throw new FileNotFoundException("Plugin assembly is not installed.", "Missing.Plugin.dll");
        }
    }

    private sealed class FatalElement : Element
    {
        public FatalElement()
        {
            throw new AccessViolationException("Fatal plugin failure.");
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class ElementReferenceHolder : EngineObject
    {
        public ElementReferenceHolder()
        {
            ScanProperties<ElementReferenceHolder>();
        }

        public IProperty<Reference<Element>> Target { get; } = Property.Create<Reference<Element>>();

        public IProperty<Element?> ExpressionTarget { get; } = Property.Create<Element?>();
    }

    [SuppressResourceClassGeneration]
    public sealed class CustomReferenceHolder : EngineObject
    {
        public CustomReferenceHolder()
        {
            ScanProperties<CustomReferenceHolder>();
        }

        public IProperty<IReference?> Target { get; } = Property.Create<IReference?>();
    }

    [SuppressResourceClassGeneration]
    public sealed class OptionalReferenceHolder : EngineObject
    {
        public OptionalReferenceHolder()
        {
            ScanProperties<OptionalReferenceHolder>();
        }

        public IProperty<Optional<Reference<Element>>> Target { get; }
            = Property.Create<Optional<Reference<Element>>>();
    }

    [SuppressResourceClassGeneration]
    public sealed class NestedReferenceHolder : EngineObject
    {
        public NestedReferenceHolder()
        {
            ScanProperties<NestedReferenceHolder>();
        }

        public IProperty<List<Reference<Element>>> ListTargets { get; }
            = Property.Create<List<Reference<Element>>>();

        public IProperty<Dictionary<string, Reference<Element>>> DictionaryTargets { get; }
            = Property.Create<Dictionary<string, Reference<Element>>>();

        public IProperty<ReadOnlyCollection<Reference<Element>>> ReadOnlyTargets { get; }
            = Property.Create<ReadOnlyCollection<Reference<Element>>>();

        public IProperty<Reference<Element>> AnimatedTarget { get; }
            = Property.CreateAnimatable<Reference<Element>>();
    }

    [SuppressResourceClassGeneration]
    public sealed class WrappedReferenceHolder : EngineObject
    {
        public WrappedReferenceHolder()
        {
            ScanProperties<WrappedReferenceHolder>();
        }

        public IProperty<ReferenceEnvelope?> Target { get; }
            = Property.Create<ReferenceEnvelope?>();

        public IProperty<ReferenceEnvelope?> AliasTarget { get; }
            = Property.Create<ReferenceEnvelope?>();

        public IProperty<EqualityIgnoringReferenceEnvelope?> EqualityTarget { get; }
            = Property.Create<EqualityIgnoringReferenceEnvelope?>();

        public IProperty<EqualityIgnoringReferenceEnvelope?> AnimatedTarget { get; }
            = Property.CreateAnimatable<EqualityIgnoringReferenceEnvelope?>();

        public IProperty<PassiveReferenceEnvelope?> PassiveTarget { get; }
            = Property.Create<PassiveReferenceEnvelope?>();

        public IProperty<InvalidReferenceEnvelope?> InvalidTarget { get; }
            = Property.Create<InvalidReferenceEnvelope?>();

        public IProperty<CyclicReferenceEnvelope?> CyclicTarget { get; }
            = Property.Create<CyclicReferenceEnvelope?>();

        public IProperty<OrderedCyclicReferenceEnvelope?> OrderedCycleTarget { get; }
            = Property.Create<OrderedCyclicReferenceEnvelope?>();
    }

    public sealed class ReferenceEnvelope(
        Reference<Element> target,
        Optional<Reference<Element>> optionalTarget,
        string state) : IReferenceRewritable
    {
        public Reference<Element> Target { get; private set; } = target;

        public Optional<Reference<Element>> OptionalTarget { get; private set; } = optionalTarget;

        public string State { get; } = state;

        public IReferenceRewritable CreateReferenceRewriteTarget()
            => new ReferenceEnvelope(Target, OptionalTarget, State);

        public void RewriteReferences(IReferenceRewriteContext context)
        {
            Target = context.Rewrite(Target);
            OptionalTarget = context.Rewrite(OptionalTarget);
        }
    }

    public sealed record PassiveReferenceEnvelope(Reference<Element> Target, string State);

    public sealed class EqualityIgnoringReferenceEnvelope(
        Reference<Element> target,
        string state) : IReferenceRewritable
    {
        public Reference<Element> Target { get; private set; } = target;

        public string State { get; } = state;

        public IReferenceRewritable CreateReferenceRewriteTarget()
            => new EqualityIgnoringReferenceEnvelope(Target, State);

        public void RewriteReferences(IReferenceRewriteContext context)
        {
            Target = context.Rewrite(Target);
        }

        public override bool Equals(object? obj)
        {
            return obj is EqualityIgnoringReferenceEnvelope other && State == other.State;
        }

        public override int GetHashCode()
        {
            return State.GetHashCode(StringComparison.Ordinal);
        }
    }

    public sealed record InvalidReferenceEnvelope(Reference<Element> Target) : IReferenceRewritable
    {
        public IReferenceRewritable CreateReferenceRewriteTarget()
            => new CyclicReferenceEnvelope(Target);

        public void RewriteReferences(IReferenceRewriteContext context)
            => throw new InvalidOperationException("An invalid target must not be populated.");
    }

    public sealed class CyclicReferenceEnvelope(Reference<Element> target) : IReferenceRewritable
    {
        public Reference<Element> Target { get; private set; } = target;

        public CyclicReferenceEnvelope? Self { get; set; }

        public IReferenceRewritable CreateReferenceRewriteTarget()
        {
            return new CyclicReferenceEnvelope(Target)
            {
                Self = Self,
            };
        }

        public void RewriteReferences(IReferenceRewriteContext context)
        {
            Target = context.Rewrite(Target);
            Self = context.Rewrite(Self);
        }
    }

    public sealed class OrderedCyclicReferenceEnvelope(Reference<Element> target) : IReferenceRewritable
    {
        public Reference<Element> Target { get; private set; } = target;

        public OrderedCyclicReferenceEnvelope? Next { get; set; }

        public IReferenceRewritable CreateReferenceRewriteTarget()
        {
            return new OrderedCyclicReferenceEnvelope(Target)
            {
                Next = Next,
            };
        }

        public void RewriteReferences(IReferenceRewriteContext context)
        {
            Next = context.Rewrite(Next);
            Target = context.Rewrite(Target);
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class DictionaryTransformHolder : EngineObject
    {
        public DictionaryTransformHolder()
        {
            ScanProperties<DictionaryTransformHolder>();
        }

        public IProperty<Dictionary<string, Transform>> Transforms { get; }
            = Property.Create<Dictionary<string, Transform>>();
    }

    [SuppressResourceClassGeneration]
    public sealed class ManuallySerializedTransformHolder : EngineObject
    {
        public Transform? HiddenTransform { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(HiddenTransform), HiddenTransform);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            HiddenTransform = context.GetValue<Transform>(nameof(HiddenTransform));
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class TransformReferenceHolder : EngineObject
    {
        public TransformReferenceHolder()
        {
            ScanProperties<TransformReferenceHolder>();
        }

        public IProperty<Reference<Transform>> Target { get; } = Property.Create<Reference<Transform>>();
    }

    [SuppressResourceClassGeneration]
    public sealed class DrawableReferenceHolder : EngineObject
    {
        public DrawableReferenceHolder()
        {
            ScanProperties<DrawableReferenceHolder>();
        }

        public IProperty<Reference<Drawable>> Target { get; } = Property.Create<Reference<Drawable>>();
    }

    public sealed class RegisteredRecoveryElement : Element
    {
        public static readonly CoreProperty<Transform?> PluginTransformProperty;
        public static readonly CoreProperty<Reference<Element>> PluginTargetProperty;
        public static readonly CoreProperty<EqualityIgnoringReferenceEnvelope?> PluginWrapperProperty;

        static RegisteredRecoveryElement()
        {
            PluginTransformProperty = ConfigureProperty<Transform?, RegisteredRecoveryElement>(
                    nameof(PluginTransform))
                .Register();
            PluginTargetProperty = ConfigureProperty<Reference<Element>, RegisteredRecoveryElement>(
                    nameof(PluginTarget))
                .Register();
            PluginWrapperProperty = ConfigureProperty<
                    EqualityIgnoringReferenceEnvelope?,
                    RegisteredRecoveryElement>(nameof(PluginWrapper))
                .Register();
        }

        public Transform? PluginTransform
        {
            get => GetValue(PluginTransformProperty);
            set => SetValue(PluginTransformProperty, value);
        }

        public Reference<Element> PluginTarget
        {
            get => GetValue(PluginTargetProperty);
            set => SetValue(PluginTargetProperty, value);
        }

        public EqualityIgnoringReferenceEnvelope? PluginWrapper
        {
            get => GetValue(PluginWrapperProperty);
            set => SetValue(PluginWrapperProperty, value);
        }
    }

    public sealed class RegisteredOptionalRecoveryElement : Element
    {
        public static readonly CoreProperty<Optional<Transform>> PluginTransformProperty;

        static RegisteredOptionalRecoveryElement()
        {
            PluginTransformProperty = ConfigureProperty<Optional<Transform>, RegisteredOptionalRecoveryElement>(
                    nameof(PluginTransform))
                .Register();
        }

        public Optional<Transform> PluginTransform
        {
            get => GetValue(PluginTransformProperty);
            set => SetValue(PluginTransformProperty, value);
        }
    }

    public sealed class RegisteredRecoveryScene : Scene
    {
        public static readonly CoreProperty<Reference<Element>> PluginTargetProperty;

        static RegisteredRecoveryScene()
        {
            PluginTargetProperty = ConfigureProperty<Reference<Element>, RegisteredRecoveryScene>(
                    nameof(PluginTarget))
                .Register();
        }

        public Reference<Element> PluginTarget
        {
            get => GetValue(PluginTargetProperty);
            set => SetValue(PluginTargetProperty, value);
        }
    }

    private sealed class CustomReferenceExpression : IReferenceExpression
    {
        public CustomReferenceExpression(Guid objectId)
        {
            ObjectId = objectId;
        }

        public Guid ObjectId { get; }

        public string PropertyPath => string.Empty;

        public bool HasPropertyPath => false;

        public string ExpressionString => ObjectId.ToString();

        public Type ResultType => typeof(Element);

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public IReferenceExpression? Rebind(Guid objectId) => null;
    }

    private sealed class StatefulReferenceExpression(Guid objectId, string propertyPath) : IReferenceExpression
    {
        public Guid ObjectId { get; } = objectId;

        public string PropertyPath { get; } = propertyPath;

        public bool HasPropertyPath => !string.IsNullOrEmpty(PropertyPath);

        public string ExpressionString => $"{ObjectId}.{PropertyPath}";

        public Type ResultType => typeof(Element);

        public string State { get; init; } = string.Empty;

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public IReferenceExpression? Rebind(Guid objectId) => null;
    }

    private sealed class ConstructorlessReference(Guid id, Type objectType, string marker) : IReference
    {
        public Guid Id { get; } = id;

        public CoreObject? Value => null;

        public bool IsNull => Id == Guid.Empty;

        public Type ObjectType { get; } = objectType;

        public string Marker { get; } = marker;

        public IReference Resolved(CoreObject obj)
        {
            return new ConstructorlessReference(obj.Id, ObjectType, Marker);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "malformed-element-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Serialize_UriLessSceneWithEmbeddedElements_Succeeds()
    {
        var options = new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
        };

        JsonObject? json = null;
        Assert.DoesNotThrow(() => json = CoreSerializer.SerializeToJsonObject(new Scene(), options));

        Assert.That(json!["Elements"], Is.Not.Null);
    }

    [Test]
    public void Save_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        recovered.Children.Single().Name = "Recovered placeholder";
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void Save_UnresolvableKeyFrameEasing_PreservesSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Element element = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var shape = (RectShape)element.Objects.Single();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 32,
        });
        shape.Width.Animation = animation;
        CoreSerializer.StoreToUri(element, element.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject keyFrameJson = FindObjectWithProperty(json, nameof(KeyFrame.Easing))!;
        keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var recoveredShape = (RectShape)recovered.Children.Single().Objects.Single();
        var recoveredAnimation = (KeyFrameAnimation<float>)recoveredShape.Width.Animation!;
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recoveredAnimation.KeyFrames.Single().Easing, Is.InstanceOf<LinearEasing>());
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Save_RepairedFallbackAndKeyFrameEasing_ResumesPersistenceAfterBothRepairs()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Element element = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var shape = (RectShape)element.Objects.Single();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 32,
        });
        shape.Width.Animation = animation;
        element.AddObject(new RectShape());
        CoreSerializer.StoreToUri(element, element.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject keyFrameJson = FindObjectWithProperty(json, nameof(KeyFrame.Easing))!;
        keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
        json[nameof(Element.Objects)]!.AsArray()[1]!.AsObject()["$type"]
            = "[Beutl.Engine]Beutl.Graphics.Shapes:MissingShape";
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recovered.Children.Single();
        var recoveredShape = (RectShape)recoveredElement.Objects[0];
        var recoveredAnimation = (KeyFrameAnimation<float>)recoveredShape.Width.Animation!;
        Assert.That(recoveredElement.Objects[1], Is.InstanceOf<IFallback>());

        recoveredElement.Objects[1] = new RectShape();
        SuppressedStorageSource? blocked = Scene.TryResumeElementPersistence(recoveredElement);
        recoveredAnimation.KeyFrames.Single().Easing = new SplineEasing();
        SuppressedStorageSource? resumed = Scene.TryResumeElementPersistence(recoveredElement);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.Null);
            Assert.That(recoveredAnimation.KeyFrames.Single().Easing, Is.InstanceOf<SplineEasing>());
            Assert.That(resumed, Is.Not.Null);
            Assert.That(resumed!.HasNonFallbackIncidents, Is.True);
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            Assert.That(recoveredElement.Objects.OfType<IFallback>(), Is.Empty);
            Assert.That(File.ReadAllBytes(elementPath), Is.Not.EqualTo(originalBytes));
        });
    }

    [TestCase("$type")]
    [TestCase("@type")]
    public void Restore_UnresolvableTopLevelType_UsesTypeNotFoundReason(string discriminatorKey)
    {
        const string MissingType = "[Missing.Assembly]Missing.Namespace:MissingElement";
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            $$"""{"{{discriminatorKey}}":"{{MissingType}}","Id":"{{Guid.NewGuid()}}"}""");

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        var fallback = (IFallback)recovered.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(fallback.Reason, Is.EqualTo(FallbackReason.TypeNotFound));
            Assert.That(fallback.ErrorMessage, Is.Null);
            Assert.That(fallback.Json!["$type"]!.GetValue<string>(), Is.EqualTo(MissingType));
        });
    }

    [Test]
    public void Restore_TopLevelTypeScannerPrefersDollarType()
    {
        const string PreferredType = "[Missing.Assembly]Missing.Namespace:PreferredElement";
        const string LegacyType = "[Missing.Assembly]Missing.Namespace:LegacyElement";
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            $$"""{"@type":"{{LegacyType}}","$type":"{{PreferredType}}","Id":"{{Guid.NewGuid()}}"}""");

        var fallback = (IFallback)CoreSerializer.RestoreFromUri<Scene>(sceneUri)
            .Children.Single()
            .Objects.Single();

        Assert.That(fallback.Json!["$type"]!.GetValue<string>(), Is.EqualTo(PreferredType));
    }

    [Test]
    public void Restore_MalformedElementWithoutReadableId_UsesStableId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ this is not valid JSON");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_SyntacticallyValidElementAdoptsEscapedTopLevelId()
    {
        var expectedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"\u0049d":"\u0061aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","$type":"[Missing.Assembly]Missing.Namespace:Element"}""");

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.That(recovered.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void Restore_MalformedElementAdoptsEscapedTopLevelId()
    {
        var expectedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"\u0049d":"\u0061aaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","Objects":[""");

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.That(recovered.Id, Is.EqualTo(expectedId));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Restore_MalformedBomEncodedElementAdoptsTopLevelIdAndPreservesBytes(
        bool utf32,
        bool bigEndian)
    {
        Guid expectedId = Guid.NewGuid();
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Encoding encoding = utf32
            ? new UTF32Encoding(bigEndian, byteOrderMark: true, throwOnInvalidCharacters: true)
            : new UnicodeEncoding(bigEndian, byteOrderMark: true, throwOnInvalidBytes: true);
        byte[] encodedText = encoding.GetBytes($$"""{"Id":"{{expectedId}}","Objects":[""");
        byte[] rawBytes = encoding.GetPreamble().Concat(encodedText).ToArray();
        File.WriteAllBytes(elementPath, rawBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        CoreSerializer.StoreToUri(recovered, recovered.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Id, Is.EqualTo(expectedId));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(rawBytes));
        });
    }

    [Test]
    public void Restore_MalformedElementWithEmptyTopLevelId_UsesStableNonEmptyId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"Id":"00000000-0000-0000-0000-000000000000","Objects":[""");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_MalformedElementWithOnlyNestedId_DoesNotAdoptIt()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var nestedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        File.WriteAllText(
            elementPath,
            $$"""{"Objects":[{"Id":"{{nestedId}}"}],"Broken":[""");

        Guid recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.Not.EqualTo(nestedId));
            Assert.That(recovered, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public void Restore_TrailingRootId_DoesNotOverridePathDerivedId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ this is not valid JSON");
        Guid pathDerivedId = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        var trailingId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        File.WriteAllText(elementPath, $$"""{} {"Id":"{{trailingId}}"}""");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(pathDerivedId));
            Assert.That(first, Is.Not.EqualTo(trailingId));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_ResolvableNonElementDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[Beutl.Engine]Beutl.Graphics.Shapes:RectShape","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_UnparsableDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"x","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_UnresolvableGenericDiscriminator_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[NoSuchAssembly]Ns:Foo<[System.Private.CoreLib]System:Int32>","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_ElementConstructorIOException_PropagatesWrappedFailure()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var json = new JsonObject
        {
            ["$type"] = TypeFormat.ToString(typeof(IOExceptionElement)),
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
        };
        File.WriteAllText(elementPath, json.ToJsonString());

        Exception? exception = Assert.Catch(() => CoreSerializer.RestoreFromUri<Scene>(sceneUri));

        Assert.That(exception, Is.Not.Null);
        Assert.That(ContainsException<IOException>(exception!), Is.True);
    }

    [Test]
    public void Restore_ElementConstructorMissingAssembly_RecoversFallback()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var json = new JsonObject
        {
            ["$type"] = TypeFormat.ToString(typeof(MissingAssemblyElement)),
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
        };
        File.WriteAllText(elementPath, json.ToJsonString());

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(recovered.IsEnabled, Is.False);
            Assert.That(recovered.Objects, Has.One.InstanceOf<IFallback>());
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
        });
    }

    [Test]
    public void Restore_ElementConstructorFatalFailure_PropagatesWrappedFailure()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var json = new JsonObject
        {
            ["$type"] = TypeFormat.ToString(typeof(FatalElement)),
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
        };
        File.WriteAllText(elementPath, json.ToJsonString());

        Exception? exception = Assert.Catch(() => CoreSerializer.RestoreFromUri<Scene>(sceneUri));

        Assert.That(exception, Is.Not.Null);
        Assert.That(ContainsException<AccessViolationException>(exception!), Is.True);
    }

    [Test]
    public void Restore_SceneDiscriminatorWithSelfInclude_RecoversWithoutRecursing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":"[Beutl.ProjectSystem]:Scene","Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0","Elements":{"Include":["element.belm"]}}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_NestedSceneDiscriminatorWithSelfInclude_RecoversWithoutRecursing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        elementJson[nameof(Element.Objects)] = new JsonArray(new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Scene",
            [nameof(CoreObject.Uri)] = "element.belm",
            ["Elements"] = new JsonObject
            {
                ["Include"] = new JsonArray("element.belm"),
            },
        });
        File.WriteAllText(elementPath, elementJson.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().Objects.Single(), Is.InstanceOf<IFallback>());
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void DeserializeFromJsonObject_UnassignableDiscriminatorUsesFallback()
    {
        var json = new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Scene",
        };

        object restored = CoreSerializer.DeserializeFromJsonObject(json, typeof(EngineObject));

        Assert.That(restored, Is.InstanceOf<IFallback>());
    }

    [Test]
    public void SaveAs_PreservesBomAndNonUtf8SidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes =
        [
            0xEF, 0xBB, 0xBF,
            .. "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8,
            0xFF, 0xFE, 0x00,
        ];
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Id, Is.EqualTo(new Guid("85f4d478-e16d-4cb1-ab71-ee1a90a03fe0")));
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
        });
    }

    [Test]
    public void StoreToUri_AfterRehome_RecreatesMissingProtectedSource()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        // A failed multi-file save-as rolls Uri back to the original; the next ordinary save must
        // still treat the original sidecar as the protected source, not a rehome target.
        File.Delete(elementPath);
        CoreSerializer.StoreToUri(recovered, new Uri(elementPath));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(elementPath), Is.True);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
        });
    }

    [Test]
    public void StoreToUri_LeavesExternallyRepairedSourceSidecarUntouched()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        byte[] repairedBytes = "{\"Objects\":[]}"u8.ToArray();
        File.WriteAllBytes(elementPath, repairedBytes);

        CoreSerializer.StoreToUri(recovered, recovered.Uri!);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(repairedBytes));
    }

    [Test]
    public void ReferenceExpression_Rebind_ReturnsNullForUnsupportedCustomImplementation()
    {
        var expression = new CustomReferenceExpression(Guid.NewGuid());

        Assert.That(((IReferenceExpression)expression).Rebind(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public void ReferenceExpression_Rebind_DoesNotGuessHowToPreserveCustomState()
    {
        var expression = new StatefulReferenceExpression(Guid.NewGuid(), "Value")
        {
            State = "plugin-state",
        };

        Assert.That(((IReferenceExpression)expression).Rebind(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public void StoreToUri_RecoveredElementNonFileDestinationMatchesNormalFailure()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ this is not valid JSON");
        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        var destination = new Uri("https://example.com/element.belm");

        JsonException normalException = Assert.Throws<JsonException>(
            () => CoreSerializer.StoreToUri(new Element(), destination))!;
        JsonException recoveredException = Assert.Throws<JsonException>(
            () => CoreSerializer.StoreToUri(recovered, destination))!;

        Assert.That(recoveredException.GetType(), Is.EqualTo(normalException.GetType()));
    }

    [Test]
    public void Restore_NonStringDiscriminator_RecoversInsteadOfLoadingLegacyDefault()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(
            elementPath,
            """{"$type":123,"Id":"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0"}""");
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(
                recovered.Children.Single().Id,
                Is.EqualTo(new Guid("85f4d478-e16d-4cb1-ab71-ee1a90a03fe0")));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_InvalidElementScalarValue_RecoversInsteadOfFailing()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(Element.AccentColor)] = "not-a-color";
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Children.Single().IsEnabled, Is.False);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_IdlessFallbackOutsideHierarchy_ProjectsRuntimeIdAndPreservesSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Scene loaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element loadedElement = loaded.Children.Single();
        var shape = (RectShape)loadedElement.Objects.Single();
        shape.Transform.CurrentValue = new RotationTransform();
        CoreSerializer.StoreToUri(loadedElement, loadedElement.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "Transformation")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson.Remove(nameof(CoreObject.Id));
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var recoveredShape = (RectShape)recovered.Children.Single().Objects.Single();
        var fallback = (IFallback)recoveredShape.Transform.CurrentValue!;
        var fallbackObject = (CoreObject)fallback;
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(fallback.Json![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.EqualTo(fallbackObject.Id.ToString()));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_DuplicateTopLevelId_YieldsToTheHealthyElement()
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        string element1Path = Path.Combine(_root, "element1.belm");
        string element2Path = Path.Combine(_root, "element2.belm");
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        scene.Children.Add(new Element
        {
            Name = "One",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(element1Path),
        });
        scene.Children.Add(new Element
        {
            Name = "Two",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(element2Path),
        });
        Guid healthyId = scene.Children[0].Id;
        CoreSerializer.StoreToUri(scene, sceneUri);
        File.WriteAllText(element2Path, $$"""{"Id":"{{healthyId}}","Objects":[""");

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        Element healthy = firstLoad.Children.Single(c => c.Name == "One");
        Element corrupt = firstLoad.Children.Single(c => c.Name != "One");
        Element corruptAgain = secondLoad.Children.Single(c => c.Name != "One");
        Assert.Multiple(() =>
        {
            Assert.That(healthy.Id, Is.EqualTo(healthyId));
            Assert.That(corrupt.Id, Is.Not.EqualTo(healthyId));
            Assert.That(corrupt.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(corruptAgain.Id, Is.EqualTo(corrupt.Id));
        });
    }

    [Test]
    public void Restore_RecoveredElementIdWinsOwnDescendantAndPreservesGroup()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("recovered.belm", "healthy.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Element healthySource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        Guid contestedId = recoveredSource.Id;
        var recoveredShapeSource = (RectShape)recoveredSource.Objects.Single();
        recoveredShapeSource.Transform.CurrentValue = new RotationTransform { Id = contestedId };
        source.Groups.Add(ImmutableHashSet.Create(contestedId, healthySource.Id));
        CoreSerializer.StoreToUri(source, sceneUri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        json[nameof(CoreObject.Id)] = contestedId.ToString();
        JsonObject transformJson = FindObjectByDiscriminator(json, nameof(RotationTransform))!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson[nameof(CoreObject.Id)] = contestedId.ToString();
        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element firstRecovered = firstLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Guid firstDescendantId = ((CoreObject)GetTransformFallback(firstLoad, elementPaths[0])).Id;
        CoreSerializer.StoreToUri(firstLoad, sceneUri);

        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element secondRecovered = secondLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Guid secondDescendantId = ((CoreObject)GetTransformFallback(secondLoad, elementPaths[0])).Id;

        Assert.Multiple(() =>
        {
            Assert.That(firstRecovered.Id, Is.EqualTo(contestedId));
            Assert.That(secondRecovered.Id, Is.EqualTo(contestedId));
            Assert.That(firstDescendantId, Is.Not.EqualTo(contestedId));
            Assert.That(secondDescendantId, Is.EqualTo(firstDescendantId));
            Assert.That(firstLoad.Groups, Has.Count.EqualTo(1));
            Assert.That(firstLoad.Groups.Single(),
                Is.EqualTo(ImmutableHashSet.Create(contestedId, healthySource.Id)));
            Assert.That(secondLoad.Groups, Has.Count.EqualTo(1));
            Assert.That(secondLoad.Groups.Single(),
                Is.EqualTo(ImmutableHashSet.Create(contestedId, healthySource.Id)));
        });
    }

    [Test]
    public void Restore_TopLevelIdMatchingSceneId_IsReassignedStably()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Guid sceneId = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Id;
        File.WriteAllText(elementPath, $$"""{"Id":"{{sceneId}}","Objects":[""");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(sceneId));
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_TopLevelIdMatchingTimelineLayerId_IsReassignedStably()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var layer = new TimelineLayer { Id = Guid.NewGuid(), ZIndex = 1 };
        source.Layers.Add(layer);
        CoreSerializer.StoreToUri(source, sceneUri);
        File.WriteAllText(elementPath, $$"""{"Id":"{{layer.Id}}","Objects":[""");

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid first = firstLoad.Children.Single().Id;
        Guid second = secondLoad.Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(firstLoad.Layers.Single().Id, Is.EqualTo(layer.Id));
            Assert.That(first, Is.Not.EqualTo(layer.Id));
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_TopLevelIdMatchingHealthyDescendantId_IsReassignedStably()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene original = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid descendantId = original.Children
            .Single(child => child.Uri!.LocalPath == elementPaths[0])
            .Objects
            .Single()
            .Id;
        File.WriteAllText(
            elementPaths[1],
            $$"""{"Id":"{{descendantId}}","Objects":[""");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children
            .Single(child => child.Uri!.LocalPath == elementPaths[1])
            .Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children
            .Single(child => child.Uri!.LocalPath == elementPaths[1])
            .Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(descendantId));
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void Restore_RecoveredElementsSharingTopLevelId_AreAssignedStableUniqueIdsByPath()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("a.belm", "b.belm");
        var contestedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        foreach (string elementPath in elementPaths)
        {
            File.WriteAllText(elementPath, $$"""{"Id":"{{contestedId}}","Objects":[""");
        }

        Dictionary<string, Guid> first = GetIdsBySidecarName(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri));
        Dictionary<string, Guid> second = GetIdsBySidecarName(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri));

        Assert.Multiple(() =>
        {
            Assert.That(first.Values, Is.Unique);
            Assert.That(second.Values, Is.Unique);
            Assert.That(first["a.belm"], Is.EqualTo(contestedId));
            Assert.That(first["b.belm"], Is.Not.EqualTo(contestedId));
            Assert.That(second["a.belm"], Is.EqualTo(first["a.belm"]));
            Assert.That(second["b.belm"], Is.EqualTo(first["b.belm"]));
        });
    }

    [Test]
    public void Restore_RecoveredReplacementIdCollision_DerivesStableUniqueCandidate()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("a.belm", "b.belm");
        string aPath = elementPaths[0];
        string bPath = elementPaths[1];
        File.WriteAllText(bPath, "{ this is not valid JSON");

        Guid bPathId = GetIdsBySidecarName(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri))["b.belm"];
        File.WriteAllText(aPath, $$"""{"Id":"{{bPathId}}","Objects":[""");

        Dictionary<string, Guid> first = GetIdsBySidecarName(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri));
        Dictionary<string, Guid> second = GetIdsBySidecarName(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri));

        Assert.Multiple(() =>
        {
            Assert.That(first.Values, Is.Unique);
            Assert.That(second.Values, Is.Unique);
            Assert.That(first["a.belm"], Is.EqualTo(bPathId));
            Assert.That(first["b.belm"], Is.Not.EqualTo(bPathId));
            Assert.That(second["a.belm"], Is.EqualTo(first["a.belm"]));
            Assert.That(second["b.belm"], Is.EqualTo(first["b.belm"]));
        });
    }

    [Test]
    public void Restore_MalformedSubdirectoryElementIdUsesForwardSlashRelativePath()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements(Path.Combine("subdirectory", "clip.belm"));
        File.WriteAllText(elementPaths[0], "{ this is not valid JSON");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new Guid("b23f930b-40c4-51ca-a013-59c3c3798f02")));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.Not.EqualTo(new Guid("10a2473b-45a8-5459-a5e2-9ea28f691f53")));
        });
    }

    [Test]
    public void Restore_PersistedRecoveredRemapSurvivesClaimantRemoval()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene original = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthy = original.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        File.WriteAllText(
            elementPaths[1],
            $$"""{"Id":"{{healthy.Id}}","Objects":[""");

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recovered = recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        Guid remappedId = recovered.Id;
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        JsonObject persistedScene = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
        JsonObject persistedIds = persistedScene["RecoveredElementIds"]!.AsObject();
        recoveredScene.DeleteChild(
            recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]));
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(remappedId, Is.Not.EqualTo(healthy.Id));
            Assert.That(
                persistedIds["recovered.belm"]!.GetValue<string>(),
                Is.EqualTo(remappedId.ToString()));
            Assert.That(reloaded.Children, Has.Count.EqualTo(1));
            Assert.That(reloaded.Children.Single().Id, Is.EqualTo(remappedId));
        });
    }

    [Test]
    public void Restore_RepairedElementIdMigratesPersistedGroup()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("recovered.belm", "healthy.belm");
        File.WriteAllText(elementPaths[0], "{ this is not valid JSON");

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element placeholder = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Element healthy = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        recoveredScene.Groups.Add(ImmutableHashSet.Create(placeholder.Id, healthy.Id));
        var referenceHolder = new ElementReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Element>(placeholder.Id);
        var optionalReferenceHolder = new OptionalReferenceHolder();
        optionalReferenceHolder.Target.CurrentValue = new Optional<Reference<Element>>(
            new Reference<Element>(placeholder.Id));
        referenceHolder.ExpressionTarget.Expression = new ReferenceExpression<Element?>(placeholder.Id);
        healthy.AddObject(referenceHolder);
        healthy.AddObject(optionalReferenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Guid repairedId = Guid.NewGuid();
        var repaired = new Element
        {
            Id = repairedId,
            Name = "Repaired",
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[0]),
        };
        repaired.AddObject(new RectShape());
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        // Expression evaluation resolves through the hierarchical root; give the standalone
        // scene one, as the editor and agent sessions do in production.
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Element reloadedRepaired = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Element reloadedHealthy = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        ElementReferenceHolder reloadedHolder = reloadedHealthy.Objects
            .OfType<ElementReferenceHolder>()
            .Single();
        Reference<Element> migratedReference = reloadedHolder.Target.CurrentValue;
        Reference<Element> migratedOptionalReference = reloadedHealthy.Objects
            .OfType<OptionalReferenceHolder>()
            .Single()
            .Target.CurrentValue.Value;
        var migratedExpression = (IReferenceExpression)reloadedHolder.ExpressionTarget.Expression!;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedRepaired.Id, Is.EqualTo(repairedId));
            Assert.That(reloaded.Groups, Has.Count.EqualTo(1));
            Assert.That(reloaded.Groups.Single(),
                Is.EqualTo(ImmutableHashSet.Create(repairedId, healthy.Id)));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Value, Is.SameAs(reloadedRepaired));
            Assert.That(migratedOptionalReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedOptionalReference.Value, Is.SameAs(reloadedRepaired));
            Assert.That(migratedExpression.ObjectId, Is.EqualTo(repairedId));
            Assert.That(
                reloadedHolder.ExpressionTarget.GetValue(CompositionContext.Default),
                Is.SameAs(reloadedRepaired));
        });
    }

    [Test]
    public void Restore_RepairedElementIdCollisionPreservesPlaceholderIdentity()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("claimant.belm", "repaired.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element claimant = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        File.WriteAllText(elementPaths[1], "{ this is not valid JSON");

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element placeholder = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        Element recoveredClaimant = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Guid placeholderId = placeholder.Id;
        recoveredScene.Groups.Add(ImmutableHashSet.Create(claimant.Id, placeholderId));
        var referenceHolder = new ElementReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Element>(placeholderId);
        recoveredClaimant.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        var repaired = new Element
        {
            Id = claimant.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[1]),
        };
        repaired.AddObject(new RectShape());
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Element reloadedClaimant = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Element reloadedRepaired = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        Reference<Element> reloadedReference = reloadedClaimant.Objects
            .OfType<ElementReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Children.Select(static child => child.Id), Is.Unique);
            Assert.That(reloadedClaimant.Id, Is.EqualTo(claimant.Id));
            Assert.That(reloadedRepaired.Id, Is.EqualTo(placeholderId));
            Assert.That(reloaded.Groups.Single(),
                Is.EqualTo(ImmutableHashSet.Create(claimant.Id, placeholderId)));
            Assert.That(reloadedReference.Id, Is.EqualTo(placeholderId));
            Assert.That(reloadedReference.Value, Is.SameAs(reloadedRepaired));
        });
    }

    [Test]
    public void Restore_RepairedDescendantIdCollisionPreservesPlaceholderIdentity()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("claimant.belm", "repaired.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element claimant = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Element repairedSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var claimantShape = (RectShape)claimant.Objects.Single();
        var repairedShape = (RectShape)repairedSource.Objects.Single();
        var claimantTransform = new RotationTransform();
        claimantShape.Transform.CurrentValue = claimantTransform;
        repairedShape.Transform.CurrentValue = new RotationTransform { Id = claimantTransform.Id };
        CoreSerializer.StoreToUri(claimant, claimant.Uri!);
        CoreSerializer.StoreToUri(repairedSource, repairedSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[1]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(elementPaths[1], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredClaimant = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Guid placeholderId = ((CoreObject)GetTransformFallback(recoveredScene, elementPaths[1])).Id;
        var referenceHolder = new TransformReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Transform>(placeholderId);
        recoveredClaimant.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        var repaired = new Element
        {
            Id = repairedSource.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[1]),
        };
        var healthyShape = new RectShape();
        healthyShape.Transform.CurrentValue = new RotationTransform { Id = claimantTransform.Id };
        repaired.AddObject(healthyShape);
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Element reloadedClaimant = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Element reloadedRepaired = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        Transform reloadedClaimantTransform = ((RectShape)reloadedClaimant.Objects
            .OfType<RectShape>()
            .Single()).Transform.CurrentValue!;
        Transform reloadedRepairedTransform = ((RectShape)reloadedRepaired.Objects
            .OfType<RectShape>()
            .Single()).Transform.CurrentValue!;
        Reference<Transform> reloadedReference = reloadedClaimant.Objects
            .OfType<TransformReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedClaimantTransform.Id, Is.EqualTo(claimantTransform.Id));
            Assert.That(reloadedRepairedTransform.Id, Is.EqualTo(placeholderId));
            Assert.That(reloadedClaimantTransform.Id, Is.Not.EqualTo(reloadedRepairedTransform.Id));
            Assert.That(reloadedReference.Id, Is.EqualTo(placeholderId));
            Assert.That(reloadedReference.Value, Is.SameAs(reloadedRepairedTransform));
        });
    }

    [Test]
    public void Restore_RepairedDescendantWithNewIdMigratesPlaceholderReference()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("repaired.belm", "holder.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element repairedSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        var repairedShape = (RectShape)repairedSource.Objects.Single();
        var originalTransform = new RotationTransform();
        repairedShape.Transform.CurrentValue = originalTransform;
        CoreSerializer.StoreToUri(repairedSource, repairedSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid placeholderId = ((CoreObject)GetTransformFallback(recoveredScene, elementPaths[0])).Id;
        Element holder = recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var referenceHolder = new TransformReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Transform>(placeholderId);
        holder.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Guid repairedId = Guid.NewGuid();
        var repaired = new Element
        {
            Id = repairedSource.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[0]),
        };
        var healthyShape = new RectShape();
        healthyShape.Transform.CurrentValue = new RotationTransform { Id = repairedId };
        repaired.AddObject(healthyShape);
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Element reloadedRepaired = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Element reloadedHolder = reloaded.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[1]);
        Transform reloadedTransform = ((RectShape)reloadedRepaired.Objects.Single())
            .Transform.CurrentValue!;
        Reference<Transform> migratedReference = reloadedHolder.Objects
            .OfType<TransformReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedTransform.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Value, Is.SameAs(reloadedTransform));
        });
    }

    [Test]
    public void Restore_RepairedDescendantPathSurvivesEarlierObjectRemoval()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("repaired.belm", "holder.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element repairedSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        var firstShape = (RectShape)repairedSource.Objects.Single();
        firstShape.Transform.CurrentValue = new RotationTransform();
        var secondShape = new RectShape();
        secondShape.Transform.CurrentValue = new RotationTransform();
        repairedSource.AddObject(secondShape);
        Guid secondShapeId = secondShape.Id;
        CoreSerializer.StoreToUri(repairedSource, repairedSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        foreach (JsonNode? shapeNode in json[nameof(Element.Objects)]!.AsArray())
        {
            JsonObject transformJson = FindObjectByDiscriminator(shapeNode!, "RotationTransform")!;
            transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        }

        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Guid[] placeholderIds = recoveredElement.Objects
            .OfType<RectShape>()
            .Select(shape => ((CoreObject)shape.Transform.CurrentValue!).Id)
            .ToArray();
        Element holder = recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var referenceHolder = new TransformReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Transform>(placeholderIds[1]);
        holder.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Guid repairedId = Guid.NewGuid();
        var repaired = new Element
        {
            Id = repairedSource.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[0]),
        };
        var repairedSecondShape = new RectShape { Id = secondShapeId };
        repairedSecondShape.Transform.CurrentValue = new RotationTransform { Id = repairedId };
        repaired.AddObject(repairedSecondShape);
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Transform reloadedTransform = ((RectShape)reloaded.Children.Single(
                child => child.Uri!.LocalPath == elementPaths[0]).Objects.Single())
            .Transform.CurrentValue!;
        Reference<Transform> migratedReference = reloaded.Children.Single(
                child => child.Uri!.LocalPath == elementPaths[1]).Objects
            .OfType<TransformReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedTransform.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Id, Is.Not.EqualTo(placeholderIds[0]));
            Assert.That(migratedReference.Value, Is.SameAs(reloadedTransform));
        });
    }

    [Test]
    public void Restore_UniqueDirectFallbackSurvivesEarlierObjectRemovalAndNewId()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("repaired.belm", "holder.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element repairedSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        repairedSource.AddObject(new RectShape());
        CoreSerializer.StoreToUri(repairedSource, repairedSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        JsonObject fallbackJson = json[nameof(Element.Objects)]!.AsArray()[1]!.AsObject();
        fallbackJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Shapes:MissingShape";
        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Guid placeholderId = ((CoreObject)recoveredElement.Objects[1]).Id;
        Element holder = recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var referenceHolder = new DrawableReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Drawable>(placeholderId);
        holder.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Guid repairedId = Guid.NewGuid();
        var repaired = new Element
        {
            Id = repairedSource.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[0]),
        };
        repaired.AddObject(new RectShape { Id = repairedId });
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        var reloadedShape = (RectShape)reloaded.Children.Single(
                child => child.Uri!.LocalPath == elementPaths[0]).Objects.Single();
        Reference<Drawable> migratedReference = reloaded.Children.Single(
                child => child.Uri!.LocalPath == elementPaths[1]).Objects
            .OfType<DrawableReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedShape.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Value, Is.SameAs(reloadedShape));
        });
    }

    [Test]
    public void Restore_AmbiguousRepairedDescendantAfterEarlierRemovalDoesNotMigratePlaceholders()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("repaired.belm", "holder.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element repairedSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        var firstShape = (RectShape)repairedSource.Objects.Single();
        firstShape.Transform.CurrentValue = new RotationTransform();
        var secondShape = new RectShape();
        secondShape.Transform.CurrentValue = new RotationTransform();
        repairedSource.AddObject(secondShape);
        CoreSerializer.StoreToUri(repairedSource, repairedSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        foreach (JsonNode? shapeNode in json[nameof(Element.Objects)]!.AsArray())
        {
            JsonObject transformJson = FindObjectByDiscriminator(shapeNode!, "RotationTransform")!;
            transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        }

        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recoveredScene.Children.Single(
            child => child.Uri!.LocalPath == elementPaths[0]);
        Guid[] placeholderIds = recoveredElement.Objects
            .OfType<RectShape>()
            .Select(shape => ((CoreObject)shape.Transform.CurrentValue!).Id)
            .ToArray();
        Element holder = recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        foreach (Guid placeholderId in placeholderIds)
        {
            var referenceHolder = new TransformReferenceHolder();
            referenceHolder.Target.CurrentValue = new Reference<Transform>(placeholderId);
            holder.AddObject(referenceHolder);
        }

        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        Guid repairedId = Guid.NewGuid();
        var repaired = new Element
        {
            Id = repairedSource.Id,
            Name = "Repaired",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPaths[0]),
        };
        var repairedShape = new RectShape { Id = Guid.NewGuid() };
        repairedShape.Transform.CurrentValue = new RotationTransform { Id = repairedId };
        repaired.AddObject(repairedShape);
        CoreSerializer.StoreToUri(repaired, repaired.Uri!);

        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var application = new BeutlApplication();
        var project = new Project();
        application.Project = project;
        project.Items.Add(reloaded);
        Reference<Transform>[] references = reloaded.Children.Single(
                child => child.Uri!.LocalPath == elementPaths[1]).Objects
            .OfType<TransformReferenceHolder>()
            .Select(static item => item.Target.CurrentValue)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(references.Select(static item => item.Id), Is.EqualTo(placeholderIds));
            Assert.That(references.Select(static item => item.Value), Is.All.Null);
            Assert.That(references.Select(static item => item.Id), Does.Not.Contain(repairedId));
        });
    }

    [Test]
    public void Deserialize_PreservesBackslashesInRecoveredDescendantIdentityGraphPath()
    {
        const string IdentityKey = "element.belm!path:$/property:Objects/key:folder\\turn";
        Guid identityId = Guid.NewGuid();
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "scene.scene")) };
        JsonObject json = CoreSerializer.SerializeToJsonObject(scene);
        json["RecoveredDescendantIdentities"] = new JsonObject
        {
            [IdentityKey] = identityId.ToString(),
        };
        var restored = new Scene { Uri = scene.Uri };

        CoreSerializer.PopulateFromJsonObject(
            restored,
            typeof(Scene),
            json,
            new CoreSerializerOptions { BaseUri = scene.Uri, Mode = CoreSerializationMode.Read });

        var identities = (Dictionary<string, Guid>)typeof(Scene)
            .GetField("_recoveredDescendantIdentities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(restored)!;
        Assert.That(identities, Contains.Key(IdentityKey).WithValue(identityId));
    }

    [Test]
    public void ResolveMigratedReference_CustomReferenceWithoutGuidConstructorIsRetained()
    {
        Guid originalId = Guid.NewGuid();
        Guid migratedId = Guid.NewGuid();
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "scene.scene")) };
        var migrated = new Element
        {
            Id = migratedId,
            Uri = new Uri(Path.Combine(_root, "target.belm")),
        };
        scene.Children.Add(migrated);
        var reference = new ConstructorlessReference(originalId, typeof(RectShape), "custom");
        var holder = new CustomReferenceHolder();
        holder.Target.CurrentValue = reference;
        var owner = new Element { Uri = new Uri(Path.Combine(_root, "owner.belm")) };
        owner.AddObject(holder);
        scene.Children.Add(owner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migratedId;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.DoesNotThrow(() => method.Invoke(scene, null));

        Assert.That(holder.Target.CurrentValue, Is.SameAs(reference));
    }

    [Test]
    public void MigrateRecoveredElementReferences_TraversesContainersAndKeyFrames()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var holder = new NestedReferenceHolder();
        holder.ListTargets.CurrentValue = [new Reference<Element>(originalId)];
        holder.DictionaryTargets.CurrentValue = new Dictionary<string, Reference<Element>>
        {
            ["migrated"] = new Reference<Element>(originalId),
        };
        holder.ReadOnlyTargets.CurrentValue = new ReadOnlyCollection<Reference<Element>>(
            [new Reference<Element>(originalId)]);
        var animation = new KeyFrameAnimation<Reference<Element>>();
        animation.KeyFrames.Add(new KeyFrame<Reference<Element>>
        {
            KeyTime = TimeSpan.Zero,
            Value = new Reference<Element>(originalId),
        });
        holder.AnimatedTarget.Animation = animation;
        var owner = new Element { Uri = new Uri(Path.Combine(_root, "owner.belm")) };
        owner.AddObject(holder);
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Children.Add(owner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        Reference<Element> migratedReference
            = ((KeyFrame<Reference<Element>>)animation.KeyFrames.Single()).Value;
        Reference<Element> migratedListReference = holder.ListTargets.CurrentValue!.Single();
        Reference<Element> migratedDictionaryReference
            = holder.DictionaryTargets.CurrentValue!["migrated"];
        Reference<Element> migratedReadOnlyReference = holder.ReadOnlyTargets.CurrentValue!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(migratedListReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedListReference.Value, Is.SameAs(migrated));
            Assert.That(migratedDictionaryReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedDictionaryReference.Value, Is.SameAs(migrated));
            Assert.That(migratedReadOnlyReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedReadOnlyReference.Value, Is.SameAs(migrated));
            Assert.That(migratedReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedReference.Value, Is.SameAs(migrated));
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_RewritesOptInWrappersWithoutTouchingOtherPocos()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var serializedWrapper = new ReferenceEnvelope(
            new Reference<Element>(originalId),
            new Optional<Reference<Element>>(new Reference<Element>(originalId)),
            "plugin-state");
        string json = JsonSerializer.Serialize(serializedWrapper, JsonHelper.SerializerOptions);
        ReferenceEnvelope wrapper = JsonSerializer.Deserialize<ReferenceEnvelope>(
            json,
            JsonHelper.SerializerOptions)!;
        var passiveWrapper = new PassiveReferenceEnvelope(
            new Reference<Element>(originalId),
            "passive-state");
        var equalityWrapper = new EqualityIgnoringReferenceEnvelope(
            new Reference<Element>(originalId),
            "equality-state");
        var coreWrapper = new EqualityIgnoringReferenceEnvelope(
            new Reference<Element>(originalId),
            "core-state");
        var keyFrameWrapper = new EqualityIgnoringReferenceEnvelope(
            new Reference<Element>(originalId),
            "keyframe-state");
        var invalidWrapper = new InvalidReferenceEnvelope(new Reference<Element>(originalId));
        var cyclicWrapper = new CyclicReferenceEnvelope(new Reference<Element>(originalId));
        cyclicWrapper.Self = cyclicWrapper;
        var holder = new WrappedReferenceHolder();
        holder.Target.CurrentValue = wrapper;
        holder.AliasTarget.CurrentValue = wrapper;
        holder.EqualityTarget.CurrentValue = equalityWrapper;
        var animation = new KeyFrameAnimation<EqualityIgnoringReferenceEnvelope?>();
        var keyFrame = new KeyFrame<EqualityIgnoringReferenceEnvelope?>
        {
            KeyTime = TimeSpan.Zero,
            Value = keyFrameWrapper,
        };
        animation.KeyFrames.Add(keyFrame);
        holder.AnimatedTarget.Animation = animation;
        holder.PassiveTarget.CurrentValue = passiveWrapper;
        holder.InvalidTarget.CurrentValue = invalidWrapper;
        holder.CyclicTarget.CurrentValue = cyclicWrapper;
        var owner = new Element { Uri = new Uri(Path.Combine(_root, "owner.belm")) };
        owner.AddObject(holder);
        var registeredOwner = new RegisteredRecoveryElement
        {
            Uri = new Uri(Path.Combine(_root, "registered-owner.belm")),
            PluginWrapper = coreWrapper,
        };
        int corePropertyChanges = 0;
        int keyFrameChanges = 0;
        registeredOwner.PropertyChanged += (_, e) =>
            corePropertyChanges += e.PropertyName == nameof(RegisteredRecoveryElement.PluginWrapper) ? 1 : 0;
        keyFrame.PropertyChanged += (_, e) =>
            keyFrameChanges += e.PropertyName == nameof(IKeyFrame.Value) ? 1 : 0;
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Children.Add(owner);
        scene.Children.Add(registeredOwner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        ReferenceEnvelope rewritten = holder.Target.CurrentValue!;
        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Is.Not.SameAs(wrapper));
            Assert.That(rewritten.State, Is.EqualTo("plugin-state"));
            Assert.That(rewritten.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(rewritten.Target.Value, Is.SameAs(migrated));
            Assert.That(rewritten.OptionalTarget.Value.Id, Is.EqualTo(migrated.Id));
            Assert.That(rewritten.OptionalTarget.Value.Value, Is.SameAs(migrated));
            Assert.That(holder.AliasTarget.CurrentValue, Is.SameAs(rewritten));
            Assert.That(holder.AliasTarget.CurrentValue!.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(holder.EqualityTarget.CurrentValue, Is.Not.SameAs(equalityWrapper));
            Assert.That(holder.EqualityTarget.CurrentValue!.State, Is.EqualTo("equality-state"));
            Assert.That(holder.EqualityTarget.CurrentValue.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(registeredOwner.PluginWrapper, Is.Not.SameAs(coreWrapper));
            Assert.That(registeredOwner.PluginWrapper!.State, Is.EqualTo("core-state"));
            Assert.That(registeredOwner.PluginWrapper.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(corePropertyChanges, Is.EqualTo(1));
            Assert.That(keyFrame.Value, Is.Not.SameAs(keyFrameWrapper));
            Assert.That(keyFrame.Value!.State, Is.EqualTo("keyframe-state"));
            Assert.That(keyFrame.Value.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(keyFrameChanges, Is.EqualTo(1));
            Assert.That(holder.PassiveTarget.CurrentValue, Is.SameAs(passiveWrapper));
            Assert.That(holder.PassiveTarget.CurrentValue!.Target.Id, Is.EqualTo(originalId));
            Assert.That(holder.InvalidTarget.CurrentValue, Is.SameAs(invalidWrapper));
            Assert.That(holder.InvalidTarget.CurrentValue!.Target.Id, Is.EqualTo(originalId));
            Assert.That(holder.CyclicTarget.CurrentValue, Is.Not.SameAs(cyclicWrapper));
            Assert.That(holder.CyclicTarget.CurrentValue!.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(holder.CyclicTarget.CurrentValue.Self, Is.SameAs(holder.CyclicTarget.CurrentValue));
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_UnrelatedMigrationDoesNotRebuildCycle()
    {
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var wrapper = new CyclicReferenceEnvelope(new Reference<Element>(Guid.NewGuid()));
        wrapper.Self = wrapper;
        var holder = new WrappedReferenceHolder();
        holder.CyclicTarget.CurrentValue = wrapper;
        int changes = 0;
        holder.CyclicTarget.ValueChanged += (_, _) => changes++;
        var owner = new Element { Uri = new Uri(Path.Combine(_root, "owner.belm")) };
        owner.AddObject(holder);
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Children.Add(owner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[Guid.NewGuid()] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        Assert.Multiple(() =>
        {
            Assert.That(holder.CyclicTarget.CurrentValue, Is.SameAs(wrapper));
            Assert.That(holder.CyclicTarget.CurrentValue!.Self, Is.SameAs(wrapper));
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_RebuildsMutualCycleWhenLeafFollowsBackEdge()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var first = new OrderedCyclicReferenceEnvelope(new Reference<Element>(originalId));
        var second = new OrderedCyclicReferenceEnvelope(new Reference<Element>(Guid.NewGuid()));
        first.Next = second;
        second.Next = first;
        var holder = new WrappedReferenceHolder();
        holder.OrderedCycleTarget.CurrentValue = first;
        var owner = new Element { Uri = new Uri(Path.Combine(_root, "owner.belm")) };
        owner.AddObject(holder);
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Children.Add(owner);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        OrderedCyclicReferenceEnvelope rewritten = holder.OrderedCycleTarget.CurrentValue!;
        Assert.Multiple(() =>
        {
            Assert.That(rewritten, Is.Not.SameAs(first));
            Assert.That(rewritten.Next, Is.Not.SameAs(second));
            Assert.That(rewritten.Next!.Next, Is.SameAs(rewritten));
            Assert.That(rewritten.Target.Id, Is.EqualTo(migrated.Id));
            Assert.That(rewritten.Target.Value, Is.SameAs(migrated));
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_TraversesLayerAndMarkerGraphs()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var layerHolder = new ElementReferenceHolder();
        layerHolder.Target.CurrentValue = new Reference<Element>(originalId);
        var markerHolder = new ElementReferenceHolder();
        markerHolder.Target.CurrentValue = new Reference<Element>(originalId);
        var layer = new TimelineLayer();
        var marker = new SceneMarker();
        ((IModifiableHierarchical)layer).AddChild(layerHolder);
        ((IModifiableHierarchical)marker).AddChild(markerHolder);
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Layers.Add(layer);
        scene.Markers.Add(marker);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        Assert.Multiple(() =>
        {
            Assert.That(layerHolder.Target.CurrentValue.Id, Is.EqualTo(migrated.Id));
            Assert.That(layerHolder.Target.CurrentValue.Value, Is.SameAs(migrated));
            Assert.That(markerHolder.Target.CurrentValue.Id, Is.EqualTo(migrated.Id));
            Assert.That(markerHolder.Target.CurrentValue.Value, Is.SameAs(migrated));
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_TraversesRegisteredCoreProperties()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var holder = new RegisteredRecoveryElement
        {
            Uri = new Uri(Path.Combine(_root, "holder.belm")),
            PluginTarget = new Reference<Element>(originalId),
        };
        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "migration.scene")) };
        scene.Children.Add(migrated);
        scene.Children.Add(holder);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        Assert.Multiple(() =>
        {
            Assert.That(holder.PluginTarget.Id, Is.EqualTo(migrated.Id));
            Assert.That(holder.PluginTarget.Value, Is.SameAs(migrated));
        });
    }

    [Test]
    public void MigrateRecoveredElementReferences_TraversesRegisteredSceneProperties()
    {
        Guid originalId = Guid.NewGuid();
        var migrated = new Element
        {
            Id = Guid.NewGuid(),
            Uri = new Uri(Path.Combine(_root, "migrated.belm")),
        };
        var scene = new RegisteredRecoveryScene
        {
            Uri = new Uri(Path.Combine(_root, "migration.scene")),
            PluginTarget = new Reference<Element>(originalId),
        };
        scene.Children.Add(migrated);
        var migrations = (Dictionary<Guid, Guid>)typeof(Scene)
            .GetField("_pendingRecoveredElementIdMigrations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scene)!;
        migrations[originalId] = migrated.Id;
        MethodInfo method = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        method.Invoke(scene, null);

        Assert.Multiple(() =>
        {
            Assert.That(scene.PluginTarget.Id, Is.EqualTo(migrated.Id));
            Assert.That(scene.PluginTarget.Value, Is.SameAs(migrated));
        });
    }

    [Test]
    public void Restore_MalformedElementIdAvoidsSerializedMarkerCollision()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var marker = new SceneMarker(TimeSpan.Zero, "Marker") { Id = Guid.NewGuid() };
        source.Markers.Add(marker);
        CoreSerializer.StoreToUri(source, sceneUri);
        File.WriteAllText(elementPath, $$"""{"Id":"{{marker.Id}}","Objects":[""");

        Scene first = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid recoveredId = first.Children.Single().Id;
        CoreSerializer.StoreToUri(first, sceneUri);
        Scene second = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(first.Markers.Single().Id, Is.EqualTo(marker.Id));
            Assert.That(recoveredId, Is.Not.EqualTo(marker.Id));
            Assert.That(second.Markers.Single().Id, Is.EqualTo(marker.Id));
            Assert.That(second.Children.Single().Id, Is.EqualTo(recoveredId));
        });
    }

    [Test]
    public void Restore_KnownTypeDeserializationFallbackAdoptsSerializedId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject objectJson = json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        Guid serializedId = Guid.Parse(objectJson[nameof(CoreObject.Id)]!.GetValue<string>());
        objectJson[nameof(RectShape.Width)] = "invalid-width";
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene first = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Scene second = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var firstFallback = (CoreObject)first.Children.Single().Objects.Single();
        var secondFallback = (CoreObject)second.Children.Single().Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstFallback, Is.InstanceOf<IFallback>());
            Assert.That(firstFallback.Id, Is.EqualTo(serializedId));
            Assert.That(secondFallback.Id, Is.EqualTo(serializedId));
        });
    }

    [Test]
    public void Restore_RegisteredCorePropertyFallbackAdoptsSerializedId()
    {
        string scenePath = Path.Combine(_root, "registered.scene");
        string elementPath = Path.Combine(_root, "registered.belm");
        var element = new RegisteredRecoveryElement
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPath),
            PluginTransform = new RotationTransform(),
        };
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(scenePath) };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, nameof(RotationTransform))!;
        Guid serializedId = Guid.Parse(transformJson[nameof(CoreObject.Id)]!.GetValue<string>());
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene first = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        Scene second = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var firstElement = (RegisteredRecoveryElement)first.Children.Single();
        var secondElement = (RegisteredRecoveryElement)second.Children.Single();
        var firstFallback = (CoreObject)firstElement.PluginTransform!;
        var secondFallback = (CoreObject)secondElement.PluginTransform!;

        Assert.Multiple(() =>
        {
            Assert.That(firstFallback, Is.InstanceOf<IFallback>());
            Assert.That(firstFallback.Id, Is.EqualTo(serializedId));
            Assert.That(secondFallback.Id, Is.EqualTo(serializedId));
        });
    }

    [Test]
    public void Restore_RegisteredCorePropertyFallbackCollisionIsRemappedStably()
    {
        string scenePath = Path.Combine(_root, "registered-collision.scene");
        string healthyPath = Path.Combine(_root, "healthy.belm");
        string recoveredPath = Path.Combine(_root, "registered.belm");
        Guid claimantId = Guid.NewGuid();
        var healthy = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(healthyPath),
        };
        healthy.AddObject(new RectShape { Id = claimantId });
        var recovered = new RegisteredRecoveryElement
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(recoveredPath),
            PluginTransform = new RotationTransform { Id = claimantId },
        };
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(scenePath) };
        scene.Children.Add(healthy);
        scene.Children.Add(recovered);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(recoveredPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, nameof(RotationTransform))!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(recoveredPath, json.ToJsonString());

        Scene first = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var firstRecovered = (RegisteredRecoveryElement)first.Children.Single(
            element => element.Uri!.LocalPath == recoveredPath);
        Guid reassignedId = firstRecovered.PluginTransform!.Id;
        CoreSerializer.StoreToUri(first, scene.Uri);
        Scene second = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var secondRecovered = (RegisteredRecoveryElement)second.Children.Single(
            element => element.Uri!.LocalPath == recoveredPath);

        Assert.Multiple(() =>
        {
            Assert.That(reassignedId, Is.Not.EqualTo(claimantId));
            Assert.That(secondRecovered.PluginTransform!.Id, Is.EqualTo(reassignedId));
        });
    }

    [Test]
    public void Restore_RegisteredOptionalFallbackRepairMigratesReferences()
    {
        string scenePath = Path.Combine(_root, "registered-optional.scene");
        string recoveredPath = Path.Combine(_root, "registered-optional.belm");
        string holderPath = Path.Combine(_root, "holder.belm");
        Guid originalId = Guid.NewGuid();
        var recovered = new RegisteredOptionalRecoveryElement
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(recoveredPath),
            PluginTransform = new Optional<Transform>(
                new RotationTransform { Id = originalId }),
        };
        var referenceHolder = new TransformReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Transform>(originalId);
        var holder = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(holderPath),
        };
        holder.AddObject(referenceHolder);
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(scenePath) };
        scene.Children.Add(recovered);
        scene.Children.Add(holder);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        JsonObject json = JsonNode.Parse(File.ReadAllText(recoveredPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, nameof(RotationTransform))!;
        string transformType = transformJson["$type"]!.GetValue<string>();
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(recoveredPath, json.ToJsonString());

        Scene first = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        CoreSerializer.StoreToUri(first, scene.Uri);
        Guid repairedId = Guid.NewGuid();
        json = JsonNode.Parse(File.ReadAllText(recoveredPath))!.AsObject();
        transformJson = FindObjectByDiscriminator(json, "DoesNotExist")!;
        transformJson["$type"] = transformType;
        transformJson[nameof(CoreObject.Id)] = repairedId.ToString();
        File.WriteAllText(recoveredPath, json.ToJsonString());

        Scene second = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var repairedElement = (RegisteredOptionalRecoveryElement)second.Children.Single(
            element => element.Uri!.LocalPath == recoveredPath);
        Transform repairedTransform = repairedElement.PluginTransform.Value;
        Reference<Transform> migratedReference = second.Children.Single(
                element => element.Uri!.LocalPath == holderPath)
            .Objects.OfType<TransformReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(repairedTransform.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Value, Is.SameAs(repairedTransform));
        });
    }

    [Test]
    public void Restore_UnknownExternalObjectTypeUsesPropertyFallback()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        string transformPath = Path.Combine(_root, "external-transform.json");
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var sourceShape = (RectShape)source.Objects.Single();
        sourceShape.Transform.CurrentValue = new RotationTransform
        {
            Uri = new Uri(transformPath),
        };
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject transformJson = JsonNode.Parse(File.ReadAllText(transformPath))!.AsObject();
        transformJson["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        File.WriteAllText(transformPath, transformJson.ToJsonString());

        Transform restoredTransform = CoreSerializer.RestoreFromUri<Transform>(new Uri(transformPath));
        Element restoredElement = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recovered.Children.Single();
        RectShape? recoveredShape = recoveredElement.Objects.OfType<RectShape>().SingleOrDefault();
        string? recoveryError = recoveredElement.Objects.OfType<IFallback>().FirstOrDefault()?.ErrorMessage;

        Assert.Multiple(() =>
        {
            Assert.That(restoredTransform, Is.InstanceOf<FallbackTransform>());
            Assert.That(restoredTransform.Uri, Is.EqualTo(new Uri(transformPath)));
            Assert.That(restoredElement.Objects.OfType<RectShape>(), Has.Exactly(1).Items);
            Assert.That(recoveredElement.IsEnabled, Is.True);
            Assert.That(recoveredElement.Objects, Has.Count.EqualTo(1));
            Assert.That(recoveredShape, Is.Not.Null, recoveryError);
            Assert.That(recoveredShape?.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
            Assert.That(recoveredShape?.Transform.CurrentValue?.Uri, Is.EqualTo(new Uri(transformPath)));
        });
    }

    [Test]
    public void Restore_IncompatibleExternalObjectTypeUsesPropertyFallback()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        string transformPath = Path.Combine(_root, "external-transform.json");
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var sourceShape = (RectShape)source.Objects.Single();
        sourceShape.Transform.CurrentValue = new RotationTransform
        {
            Uri = new Uri(transformPath),
        };
        CoreSerializer.StoreToUri(source, source.Uri!);
        var incompatible = new Scene(64, 64, "Incompatible") { Uri = new Uri(transformPath) };
        CoreSerializer.StoreToUri(incompatible, incompatible.Uri);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredElement = recovered.Children.Single();
        RectShape? recoveredShape = recoveredElement.Objects.OfType<RectShape>().SingleOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(recoveredElement.IsEnabled, Is.True);
            Assert.That(recoveredShape, Is.Not.Null);
            Assert.That(recoveredShape?.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
            Assert.That(recoveredShape?.Transform.CurrentValue?.Uri, Is.EqualTo(new Uri(transformPath)));
        });
    }

    [Test]
    public void Restore_PersistedRecoveredDescendantRemapSurvivesClaimantRemoval()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthySource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Guid claimantId = healthySource.Objects.Single().Id;
        Element recoveredSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var recoveredShapeSource = (RectShape)recoveredSource.Objects.Single();
        recoveredShapeSource.Transform.CurrentValue = new RotationTransform { Id = claimantId };
        CoreSerializer.StoreToUri(recoveredSource, recoveredSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[1]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson[nameof(CoreObject.Id)] = claimantId.ToString();
        File.WriteAllText(elementPaths[1], json.ToJsonString());

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid remappedId = ((CoreObject)GetTransformFallback(recoveredScene, elementPaths[1])).Id;
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);
        recoveredScene.DeleteChild(
            recoveredScene.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]));
        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        JsonObject persistedScene = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
        JsonObject persistedIds = persistedScene["RecoveredDescendantIds"]!.AsObject();
        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid reloadedId = ((CoreObject)GetTransformFallback(reloaded, elementPaths[1])).Id;

        Assert.Multiple(() =>
        {
            Assert.That(remappedId, Is.Not.EqualTo(claimantId));
            Assert.That(
                persistedIds[$"recovered.belm!{claimantId:D}#0"]!.GetValue<string>(),
                Is.EqualTo(remappedId.ToString()));
            Assert.That(reloaded.Children, Has.Count.EqualTo(1));
            Assert.That(reloadedId, Is.EqualTo(remappedId));
        });
    }

    [Test]
    public void Restore_RecoveredDescendantsSharingSerializedIdKeepOccurrenceStableRemaps()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthySource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Guid claimantId = healthySource.Objects.Single().Id;
        Element recoveredSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        recoveredSource.AddObject(new RectShape());
        CoreSerializer.StoreToUri(recoveredSource, recoveredSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[1]))!.AsObject();
        JsonArray objects = json[nameof(Element.Objects)]!.AsArray();
        foreach (JsonObject obj in objects.OfType<JsonObject>())
        {
            obj[nameof(CoreObject.Id)] = claimantId.ToString();
        }

        objects[1]!.AsObject()["$type"] = "[Beutl.Engine]Beutl.Graphics.Shapes:DoesNotExist";
        File.WriteAllText(elementPaths[1], json.ToJsonString());

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element firstRecovered = firstLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        Guid[] firstAssignedIds = firstRecovered.Objects.Select(static obj => obj.Id).ToArray();
        Guid[] firstGraphIds = EnumerateElementGraphs(firstLoad).Select(static obj => obj.Id).ToArray();
        firstRecovered.Objects.Move(0, 1);
        CoreSerializer.StoreToUri(firstLoad, sceneUri);

        JsonObject persistedScene = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
        JsonObject persistedIds = persistedScene["RecoveredDescendantIds"]!.AsObject();
        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element secondRecovered = secondLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        Guid[] secondAssignedIds = secondRecovered.Objects.Select(static obj => obj.Id).ToArray();
        Guid[] secondGraphIds = EnumerateElementGraphs(secondLoad).Select(static obj => obj.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstGraphIds, Is.Unique);
            Assert.That(secondGraphIds, Is.Unique);
            Assert.That(firstAssignedIds, Has.Length.EqualTo(2));
            Assert.That(firstAssignedIds, Does.Not.Contain(claimantId));
            Assert.That(secondAssignedIds, Is.EqualTo(firstAssignedIds));
            Assert.That(
                persistedIds[$"recovered.belm!{claimantId:D}#0"]!.GetValue<string>(),
                Is.EqualTo(firstAssignedIds[0].ToString()));
            Assert.That(
                persistedIds[$"recovered.belm!{claimantId:D}#1"]!.GetValue<string>(),
                Is.EqualTo(firstAssignedIds[1].ToString()));
        });
    }

    [Test]
    public void Save_RebuildsRecoveredElementIdMapAfterRehome()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ this is not valid JSON");

        Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recovered = recoveredScene.Children.Single();
        Guid recoveredId = recovered.Id;
        string rehomedPath = Path.Combine(_root, "renamed.belm");
        File.Move(elementPath, rehomedPath);
        recovered.Uri = new Uri(rehomedPath);

        CoreSerializer.StoreToUri(recoveredScene, sceneUri);

        JsonObject persistedScene = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
        JsonObject persistedIds = persistedScene["RecoveredElementIds"]!.AsObject();
        Scene reloaded = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        Assert.Multiple(() =>
        {
            Assert.That(persistedIds, Has.Count.EqualTo(1));
            Assert.That(
                persistedIds["renamed.belm"]!.GetValue<string>(),
                Is.EqualTo(recoveredId.ToString()));
            Assert.That(reloaded.Children.Single().Id, Is.EqualTo(recoveredId));
        });
    }

    [Test]
    public void Restore_NestedFallbackProjection_PreservesOriginalDiscriminator()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        const string OriginalType = "[Beutl.Engine]Beutl.Graphics.Effects:NoSuchEffect";
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(Element.Objects)] = new JsonArray(new JsonObject
        {
            ["$type"] = OriginalType,
            [nameof(CoreObject.Id)] = Guid.NewGuid().ToString(),
        });
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var fallback = (IFallback)recovered.Children.Single().Objects.Single();

        Assert.That(fallback.Json!["$type"]!.GetValue<string>(), Is.EqualTo(OriginalType));
    }

    [Test]
    public void Restore_RecoveredNestedFallbackDuplicateId_IsReassignedStably()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthySource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Element recoveredSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        Guid healthyId = healthySource.Id;
        var recoveredShapeSource = (RectShape)recoveredSource.Objects.Single();
        recoveredShapeSource.Transform.CurrentValue = new RotationTransform { Id = healthyId };
        CoreSerializer.StoreToUri(recoveredSource, recoveredSource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[1]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson[nameof(CoreObject.Id)] = healthyId.ToString();
        File.WriteAllText(elementPaths[1], json.ToJsonString());
        byte[] originalBytes = File.ReadAllBytes(elementPaths[1]);

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthy = firstLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        IFallback fallback = GetTransformFallback(firstLoad, elementPaths[1]);
        var fallbackObject = (CoreObject)fallback;
        Guid reassignedId = fallbackObject.Id;
        Guid[] firstIds = EnumerateElementGraphs(firstLoad).Select(obj => obj.Id).ToArray();

        CoreSerializer.StoreToUri(firstLoad, sceneUri);
        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthyAgain = secondLoad.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        IFallback fallbackAgain = GetTransformFallback(secondLoad, elementPaths[1]);
        var fallbackObjectAgain = (CoreObject)fallbackAgain;
        Guid[] secondIds = EnumerateElementGraphs(secondLoad).Select(obj => obj.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(healthy.Id, Is.EqualTo(healthyId));
            Assert.That(firstIds, Is.Unique);
            Assert.That(firstIds, Does.Not.Contain(Guid.Empty));
            Assert.That(secondIds, Is.Unique);
            Assert.That(secondIds, Does.Not.Contain(Guid.Empty));
            Assert.That(reassignedId, Is.Not.EqualTo(healthyId));
            Assert.That(healthyAgain.Id, Is.EqualTo(healthyId));
            Assert.That(fallbackObjectAgain.Id, Is.EqualTo(reassignedId));
            Assert.That(
                fallback.Json![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.EqualTo(reassignedId.ToString()));
            Assert.That(
                fallbackAgain.Json![nameof(CoreObject.Id)]!.GetValue<string>(),
                Is.EqualTo(reassignedId.ToString()));
            Assert.That(File.ReadAllBytes(elementPaths[1]), Is.EqualTo(originalBytes));
        });
    }

    [Test]
    public void Restore_RootArrayId_DoesNotAdoptInnerIdAndRemainsStable()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var innerId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        File.WriteAllText(elementPath, $$"""[{"Id":"{{innerId}}"}]""");

        Guid first = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;
        Guid second = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single().Id;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(innerId));
            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void StoreToUri_RehomeTargetCollision_FailsWithoutRepointingOrOverwriting()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        byte[] repairedBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":[]}"u8.ToArray();
        File.WriteAllBytes(rehomedPath, repairedBytes);
        Assert.Throws<IOException>(() => CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath)));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(repairedBytes));
            Assert.That(recovered.Uri, Is.EqualTo(new Uri(elementPath)));
        });
    }

    [Test]
    public void Serialize_DoesNotMutateLongLivedRecoveryMaps()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        File.WriteAllText(elementPath, "{ malformed element");
        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var sentinelId = Guid.NewGuid();
        var elementIds = (Dictionary<string, Guid>)typeof(Scene)
            .GetField("_recoveredElementIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(recovered)!;
        var descendantIds = (Dictionary<string, Guid>)typeof(Scene)
            .GetField("_recoveredDescendantIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(recovered)!;
        var descendantIdentities = (Dictionary<string, Guid>)typeof(Scene)
            .GetField("_recoveredDescendantIdentities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(recovered)!;
        elementIds["sentinel-element"] = sentinelId;
        descendantIds["sentinel-descendant"] = sentinelId;
        descendantIdentities["sentinel-identity"] = sentinelId;

        CoreSerializer.SerializeToJsonObject(
            recovered,
            new CoreSerializerOptions { Mode = CoreSerializationMode.EmbedReferencedObjects });

        Assert.Multiple(() =>
        {
            Assert.That(elementIds["sentinel-element"], Is.EqualTo(sentinelId));
            Assert.That(descendantIds["sentinel-descendant"], Is.EqualTo(sentinelId));
            Assert.That(descendantIdentities["sentinel-identity"], Is.EqualTo(sentinelId));
        });
    }

    [Test]
    public void RemovedIdlessRecoveredDescendant_IsNotKeptAliveByRecoveryState()
    {
        (Scene scene, WeakReference descendant) = CreateDetachedIdlessRecoveredDescendant();

        for (int i = 0; i < 3 && descendant.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.That(descendant.IsAlive, Is.False);
        GC.KeepAlive(scene);
    }

    [Test]
    public void Restore_IdlessRecoveredDescendant_IsAssignedStableOccurrenceId()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("recovered.belm");
        Element source = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        var sourceShape = (RectShape)source.Objects.Single();
        sourceShape.Transform.CurrentValue = new RotationTransform();
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[0]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson.Remove(nameof(CoreObject.Id));
        File.WriteAllText(elementPaths[0], json.ToJsonString());

        Scene firstLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid firstId = ((CoreObject)GetTransformFallback(firstLoad, elementPaths[0])).Id;
        CoreSerializer.StoreToUri(firstLoad, sceneUri);

        Scene secondLoad = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Guid secondId = ((CoreObject)GetTransformFallback(secondLoad, elementPaths[0])).Id;

        Assert.Multiple(() =>
        {
            Assert.That(firstId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(secondId, Is.EqualTo(firstId));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (Scene Scene, WeakReference Descendant) CreateDetachedIdlessRecoveredDescendant()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var sourceShape = (RectShape)source.Objects.Single();
        sourceShape.Transform.CurrentValue = new RotationTransform();
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson.Remove(nameof(CoreObject.Id));
        File.WriteAllText(elementPath, json.ToJsonString());

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var descendant = (CoreObject)GetTransformFallback(scene, elementPath);
        var weakReference = new WeakReference(descendant);
        scene.Children.Clear();
        return (scene, weakReference);
    }

    [Test]
    public void TryResumeElementPersistence_DictionaryValuedFallback_StaysBlocked()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Element element = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var holder = new DictionaryTransformHolder();
        holder.Transforms.CurrentValue = new Dictionary<string, Transform>
        {
            ["rotation"] = new FallbackTransform(),
        };
        element.AddObject(holder);
        byte[] originalBytes = "{ preserved fallback bytes"u8.ToArray();
        File.WriteAllBytes(elementPath, originalBytes);
        element.SuppressedStorageSource = new SuppressedStorageSource(originalBytes, element.Uri!);

        SuppressedStorageSource? suppression = Scene.TryResumeElementPersistence(element);

        Assert.That(suppression, Is.Null);
    }

    [Test]
    public void TryResumeElementPersistence_ManuallySerializedFallbackStaysBlockedUntilRepair()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var holder = new ManuallySerializedTransformHolder
        {
            HiddenTransform = new RotationTransform(),
        };
        source.AddObject(holder);
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        File.WriteAllText(elementPath, json.ToJsonString());

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        var recoveredHolder = recovered.Objects.OfType<ManuallySerializedTransformHolder>().Single();
        Assert.That(recoveredHolder.HiddenTransform, Is.InstanceOf<IFallback>());
        recovered.Name = "Unrelated edit";

        SuppressedStorageSource? blocked = Scene.TryResumeElementPersistence(recovered);
        recoveredHolder.HiddenTransform = new RotationTransform();
        SuppressedStorageSource? resumed = Scene.TryResumeElementPersistence(recovered);

        Assert.Multiple(() =>
        {
            Assert.That(recoveredHolder.HiddenTransform, Is.Not.InstanceOf<IFallback>());
            Assert.That(blocked, Is.Null);
            Assert.That(resumed, Is.Not.Null);
            Assert.That(recovered.SuppressedStorageSource, Is.Null);
        });
    }

    [Test]
    public void Restore_CollisionRemappedRecoveredDescendant_KeepsReferenceOnSurvivingClaimant()
    {
        (Uri sceneUri, string[] elementPaths) =
            CreatePersistedSceneWithElements("healthy.belm", "recovered.belm");
        Scene source = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element healthySource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        var healthyShapeSource = (RectShape)healthySource.Objects.Single();
        healthyShapeSource.Transform.CurrentValue = new RotationTransform();
        Guid claimantId = healthyShapeSource.Transform.CurrentValue!.Id;
        Element recoveredSource = source.Children.Single(child => child.Uri!.LocalPath == elementPaths[1]);
        var recoveredShapeSource = (RectShape)recoveredSource.Objects.Single();
        recoveredShapeSource.Transform.CurrentValue = new RotationTransform { Id = claimantId };
        var referenceHolder = new TransformReferenceHolder();
        referenceHolder.Target.CurrentValue = new Reference<Transform>(claimantId);
        healthySource.AddObject(referenceHolder);
        CoreSerializer.StoreToUri(recoveredSource, recoveredSource.Uri!);
        CoreSerializer.StoreToUri(healthySource, healthySource.Uri!);

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPaths[1]))!.AsObject();
        JsonObject transformJson = FindObjectByDiscriminator(json, "RotationTransform")!;
        transformJson["$type"] = "[Beutl.Engine]Beutl.Graphics.Transformation:DoesNotExist";
        transformJson[nameof(CoreObject.Id)] = claimantId.ToString();
        File.WriteAllText(elementPaths[1], json.ToJsonString());

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recoveredHealthy = recovered.Children.Single(child => child.Uri!.LocalPath == elementPaths[0]);
        Guid remappedId = ((CoreObject)GetTransformFallback(recovered, elementPaths[1])).Id;
        var reference = (Reference<Transform>)recoveredHealthy.Objects
            .OfType<TransformReferenceHolder>()
            .Single()
            .Target.CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(remappedId, Is.Not.EqualTo(claimantId));
            // The healthy claimant still owns the original ID, so the reference must keep
            // targeting it instead of being redirected to the remapped recovered fallback.
            Assert.That(reference.Id, Is.EqualTo(claimantId));
            Assert.That(
                recoveredHealthy.Objects.OfType<RectShape>().Single().Transform.CurrentValue!.Id,
                Is.EqualTo(claimantId));
        });
    }

    [Test]
    public void ReassignDuplicateRecoveredIds_RetainsIdsFromLayerAndMarkerGraphs()
    {
        Guid layerClaimantId = Guid.NewGuid();
        Guid markerClaimantId = Guid.NewGuid();
        var layerClaimant = new RotationTransform { Id = layerClaimantId };
        var markerClaimant = new RotationTransform { Id = markerClaimantId };
        var layer = new TimelineLayer();
        var marker = new SceneMarker();
        ((IModifiableHierarchical)layer).AddChild(layerClaimant);
        ((IModifiableHierarchical)marker).AddChild(markerClaimant);

        var layerReference = new TransformReferenceHolder();
        layerReference.Target.CurrentValue = new Reference<Transform>(layerClaimantId);
        var markerReference = new TransformReferenceHolder();
        markerReference.Target.CurrentValue = new Reference<Transform>(markerClaimantId);
        var healthy = new Element { Uri = new Uri(Path.Combine(_root, "healthy.belm")) };
        healthy.AddObject(layerReference);
        healthy.AddObject(markerReference);

        var layerFallback = new FallbackTransform { Id = layerClaimantId };
        var markerFallback = new FallbackTransform { Id = markerClaimantId };
        var recovered = new Element { Uri = new Uri(Path.Combine(_root, "recovered.belm")) };
        recovered.AddObject(layerFallback);
        recovered.AddObject(markerFallback);
        recovered.SuppressedStorageSource = new SuppressedStorageSource([], recovered.Uri);

        var scene = new Scene { Uri = new Uri(Path.Combine(_root, "hierarchy.scene")) };
        scene.Layers.Add(layer);
        scene.Markers.Add(marker);
        scene.Children.Add(healthy);
        scene.Children.Add(recovered);
        MethodInfo reassign = typeof(Scene).GetMethod(
            "ReassignDuplicateRecoveredIds",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo migrate = typeof(Scene).GetMethod(
            "MigrateRecoveredElementReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        reassign.Invoke(scene, null);
        migrate.Invoke(scene, null);

        Assert.Multiple(() =>
        {
            Assert.That(layerFallback.Id, Is.Not.EqualTo(layerClaimantId));
            Assert.That(markerFallback.Id, Is.Not.EqualTo(markerClaimantId));
            Assert.That(layerReference.Target.CurrentValue.Id, Is.EqualTo(layerClaimantId));
            Assert.That(markerReference.Target.CurrentValue.Id, Is.EqualTo(markerClaimantId));
            Assert.That(layerClaimant.Id, Is.EqualTo(layerClaimantId));
            Assert.That(markerClaimant.Id, Is.EqualTo(markerClaimantId));
        });
    }

    [Test]
    public void SaveAs_CopiesRecoveredSidecarBytesToTheNewLocation()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(corruptBytes));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
            Assert.That(
                Directory.GetFiles(
                    Path.GetDirectoryName(rehomedPath)!,
                    $"{Path.GetFileName(rehomedPath)}.*.tmp"),
                Is.Empty);
        });
    }

    [Test]
    public void SaveAs_CopiesRelativeFallbackSidecarBytesToTheNewLocation()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        string referencedPath = Path.Combine(_root, "nested", "transform.json");
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var shape = (RectShape)source.Objects.Single();
        shape.Transform.CurrentValue = new RotationTransform { Uri = new Uri(referencedPath) };
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject transformJson = JsonNode.Parse(File.ReadAllText(referencedPath))!.AsObject();
        transformJson["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        File.WriteAllText(referencedPath, transformJson.ToJsonString());
        byte[] elementBytes = File.ReadAllBytes(elementPath);
        byte[] referencedBytes = File.ReadAllBytes(referencedPath);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "copy", Path.GetFileName(elementPath));
        string rehomedReferencedPath = Path.Combine(_root, "copy", "nested", "transform.json");
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));
        Element reopened = CoreSerializer.RestoreFromUri<Element>(new Uri(rehomedPath));
        var reopenedShape = (RectShape)reopened.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(elementBytes));
            Assert.That(File.ReadAllBytes(rehomedReferencedPath), Is.EqualTo(referencedBytes));
            Assert.That(reopenedShape.Transform.CurrentValue, Is.InstanceOf<FallbackTransform>());
            Assert.That(reopenedShape.Transform.CurrentValue?.Uri,
                Is.EqualTo(new Uri(rehomedReferencedPath)));
        });
    }

    [Test]
    public void SaveAs_CopiesTransitiveFallbackSidecarBytesToTheNewLocation()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        string outerPath = Path.Combine(_root, "nested", "group.json");
        string innerPath = Path.Combine(_root, "nested", "transform.json");
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var shape = (RectShape)source.Objects.Single();
        var group = new TransformGroup { Uri = new Uri(outerPath) };
        group.Children.Add(new RotationTransform { Uri = new Uri(innerPath) });
        shape.Transform.CurrentValue = group;
        CoreSerializer.StoreToUri(source, source.Uri!);

        JsonObject transformJson = JsonNode.Parse(File.ReadAllText(innerPath))!.AsObject();
        transformJson["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        File.WriteAllText(innerPath, transformJson.ToJsonString());
        byte[] elementBytes = File.ReadAllBytes(elementPath);
        byte[] outerBytes = File.ReadAllBytes(outerPath);
        byte[] innerBytes = File.ReadAllBytes(innerPath);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "copy", Path.GetFileName(elementPath));
        string rehomedOuterPath = Path.Combine(_root, "copy", "nested", "group.json");
        string rehomedInnerPath = Path.Combine(_root, "copy", "nested", "transform.json");
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));
        Element reopened = CoreSerializer.RestoreFromUri<Element>(new Uri(rehomedPath));
        var reopenedShape = (RectShape)reopened.Objects.Single();
        var reopenedGroup = (TransformGroup)reopenedShape.Transform.CurrentValue!;

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(elementBytes));
            Assert.That(File.ReadAllBytes(rehomedOuterPath), Is.EqualTo(outerBytes));
            Assert.That(File.ReadAllBytes(rehomedInnerPath), Is.EqualTo(innerBytes));
            Assert.That(reopenedGroup.Children.Single(), Is.InstanceOf<FallbackTransform>());
        });
    }

    [Test]
    public void SaveAs_DoesNotCopyRetainedSidecarsOutsideTheDestinationRoot()
    {
        string sourceDirectory = Path.Combine(_root, "source");
        string outsideDirectory = Path.Combine(_root, "outside");
        string destinationRoot = Path.Combine(_root, "destination", "project");
        Directory.CreateDirectory(sourceDirectory);
        string scenePath = Path.Combine(sourceDirectory, "scene.scene");
        string elementPath = Path.Combine(sourceDirectory, "element.belm");
        string outsideTransformPath = Path.Combine(outsideDirectory, "transform.json");
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPath),
        };
        element.AddObject(new RectShape
        {
            Transform =
            {
                CurrentValue = new RotationTransform { Uri = new Uri(outsideTransformPath) },
            },
        });
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(scenePath) };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri);

        JsonObject transformJson = JsonNode.Parse(File.ReadAllText(outsideTransformPath))!.AsObject();
        transformJson["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        File.WriteAllText(outsideTransformPath, transformJson.ToJsonString());
        Element recovered = CoreSerializer.RestoreFromUri<Scene>(scene.Uri).Children.Single();
        string rehomedPath = Path.Combine(destinationRoot, "element.belm");
        string escapedDestination = Path.Combine(_root, "destination", "outside", "transform.json");

        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(rehomedPath), Is.True);
            Assert.That(File.Exists(escapedDestination), Is.False);
        });
    }

    [Test]
    public void SaveAs_DoesNotCopyRetainedSidecarsThroughDestinationSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating directory symlinks requires additional privileges on Windows.");
        }

        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        string referencedPath = Path.Combine(_root, "nested", "transform.json");
        Element source = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        var shape = (RectShape)source.Objects.Single();
        shape.Transform.CurrentValue = new RotationTransform { Uri = new Uri(referencedPath) };
        CoreSerializer.StoreToUri(source, source.Uri!);
        JsonObject transformJson = JsonNode.Parse(File.ReadAllText(referencedPath))!.AsObject();
        transformJson["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        File.WriteAllText(referencedPath, transformJson.ToJsonString());
        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        string destinationRoot = Path.Combine(_root, "destination");
        string outsideRoot = Path.Combine(_root, "outside-symlink-target");
        Directory.CreateDirectory(destinationRoot);
        Directory.CreateDirectory(outsideRoot);
        Directory.CreateSymbolicLink(Path.Combine(destinationRoot, "nested"), outsideRoot);
        string rehomedPath = Path.Combine(destinationRoot, Path.GetFileName(elementPath));
        string escapedDestination = Path.Combine(outsideRoot, "transform.json");

        Assert.Throws<JsonException>(() =>
            CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath)));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(rehomedPath), Is.False);
            Assert.That(File.Exists(escapedDestination), Is.False);
        });
    }

    [Test]
    public void Save_PreservesDeserializationFallbackSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json[nameof(CoreObject.Name)] = "Hand-formatted element";
        json[nameof(Element.Objects)]!.AsArray()[0]!.AsObject()[nameof(RectShape.Width)] = "invalid-width";
        string fallbackSource = json.ToJsonString();
        File.WriteAllText(elementPath, fallbackSource);
        byte[] originalBytes = File.ReadAllBytes(elementPath);

        Scene recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Assert.That(recovered.Children.Single().Objects.Single(), Is.InstanceOf<IFallback>());
        CoreSerializer.StoreToUri(recovered, sceneUri);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void DirectElementSave_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        recovered.Name = "Recovered placeholder";
        CoreSerializer.StoreToUri(recovered, recovered.Uri!);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void AutoSave_PreservesMalformedElementSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(scene);
        Element recovered = scene.Children.Single();
        recovered.Name = "Recovered placeholder";
        using var service = new AutoSaveService();
        service.SaveObjects([recovered]);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void AutoSave_RemovedMalformedElementPreservesSidecarBytes()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(scene);
        Element recovered = scene.Children.Single();
        scene.Children.Remove(recovered);
        using var service = new AutoSaveService();
        service.SaveObjects([recovered]);

        Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(corruptBytes));
    }

    [Test]
    public void DeleteChild_PreservesRecoveredSidecarAndDeletesNormalSidecar()
    {
        (Uri sceneUri, string recoveredPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(recoveredPath, corruptBytes);

        Scene scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
        Element recovered = scene.Children.Single();
        string normalPath = Path.Combine(_root, "normal.belm");
        var normal = new Element
        {
            Name = "Normal",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(normalPath),
        };
        normal.AddObject(new RectShape());
        CoreSerializer.StoreToUri(normal, normal.Uri!);
        scene.Children.Add(normal);

        scene.DeleteChild(recovered);
        scene.DeleteChild(normal);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(recoveredPath), Is.EqualTo(corruptBytes));
            Assert.That(File.Exists(normalPath), Is.False);
            Assert.That(scene.Children, Is.Empty);
        });
    }

    [Test]
    public void Restore_MalformedElementPrefersTopLevelId()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        var nestedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var topLevelId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        File.WriteAllText(
            elementPath,
            $$"""{"Objects":[{"Id":"{{nestedId}}"}],"Id":"{{topLevelId}}","Broken":[""");

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();

        Assert.That(recovered.Id, Is.EqualTo(topLevelId));
    }

    private static JsonObject? FindObjectByDiscriminator(JsonNode node, string containsToken)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$type", out JsonNode? typeNode)
                && typeNode is JsonValue typeValue
                && typeValue.TryGetValue(out string? typeName)
                && typeName.Contains(containsToken))
            {
                return obj;
            }

            foreach ((string _, JsonNode? child) in obj)
            {
                if (child != null && FindObjectByDiscriminator(child, containsToken) is { } result)
                {
                    return result;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null && FindObjectByDiscriminator(child, containsToken) is { } result)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static JsonObject? FindObjectWithProperty(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey(propertyName))
            {
                return obj;
            }

            foreach ((string _, JsonNode? child) in obj)
            {
                if (child != null && FindObjectWithProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child != null && FindObjectWithProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is TException)
        {
            return true;
        }

        if (exception is AggregateException aggregate
            && aggregate.InnerExceptions.Any(ContainsException<TException>))
        {
            return true;
        }

        return exception.InnerException is { } inner && ContainsException<TException>(inner);
    }

    private static IFallback GetTransformFallback(Scene scene, string elementPath)
    {
        Element element = scene.Children.Single(child => child.Uri!.LocalPath == elementPath);
        var shape = (RectShape)element.Objects.Single();
        return (IFallback)shape.Transform.CurrentValue!;
    }

    private static IEnumerable<CoreObject> EnumerateElementGraphs(Scene scene)
    {
        foreach (Element element in scene.Children)
        {
            var objects = new List<CoreObject>();
            CollectElementGraphObjects(
                element,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                objects);

            foreach (CoreObject obj in objects)
            {
                yield return obj;
            }
        }
    }

    private static void CollectElementGraphObjects(
        object? value,
        ISet<object> visited,
        ICollection<CoreObject> objects)
    {
        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is CoreObject coreObject)
        {
            objects.Add(coreObject);
        }

        if (value is IHierarchical hierarchical)
        {
            foreach (IHierarchical child in hierarchical.HierarchicalChildren)
            {
                CollectElementGraphObjects(child, visited, objects);
            }
        }

        if (value is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                CollectElementGraphObjects(property.CurrentValue, visited, objects);
                if (property.Animation is IKeyFrameAnimation animation)
                {
                    foreach (IKeyFrame keyFrame in animation.KeyFrames)
                    {
                        CollectElementGraphObjects(keyFrame.Value, visited, objects);
                    }
                }
            }
        }

        if (value is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                CollectElementGraphObjects(item, visited, objects);
            }
        }
    }

    private (Uri SceneUri, string ElementPath) CreatePersistedScene()
    {
        (Uri sceneUri, string[] elementPaths) = CreatePersistedSceneWithElements("element.belm");
        return (sceneUri, elementPaths[0]);
    }

    private (Uri SceneUri, string[] ElementPaths) CreatePersistedSceneWithElements(
        params string[] elementFileNames)
    {
        var sceneUri = new Uri(Path.Combine(_root, "scene.scene"));
        var scene = new Scene(64, 64, "Scene")
        {
            Uri = sceneUri,
        };
        string[] elementPaths = new string[elementFileNames.Length];
        for (int i = 0; i < elementFileNames.Length; i++)
        {
            string elementPath = Path.Combine(_root, elementFileNames[i]);
            var element = new Element
            {
                Name = Path.GetFileNameWithoutExtension(elementPath),
                Start = TimeSpan.FromSeconds(i),
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(new RectShape
            {
                Width = { CurrentValue = 32 },
                Height = { CurrentValue = 32 },
            });
            scene.Children.Add(element);
            elementPaths[i] = elementPath;
        }

        CoreSerializer.StoreToUri(scene, sceneUri);
        return (sceneUri, elementPaths);
    }

    private static Dictionary<string, Guid> GetIdsBySidecarName(Scene scene)
    {
        return scene.Children.ToDictionary(
            child => Path.GetFileName(child.Uri!.LocalPath),
            child => child.Id,
            StringComparer.Ordinal);
    }
}
