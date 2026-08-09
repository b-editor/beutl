using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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

        public IProperty<Reference<Element>> AnimatedTarget { get; }
            = Property.CreateAnimatable<Reference<Element>>();
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
        referenceHolder.ExpressionTarget.Expression = new ReferenceExpression<Element?>(placeholder.Id);
        healthy.AddObject(referenceHolder);
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
        var migratedExpression = (IReferenceExpression)reloadedHolder.ExpressionTarget.Expression!;

        Assert.Multiple(() =>
        {
            Assert.That(reloadedRepaired.Id, Is.EqualTo(repairedId));
            Assert.That(reloaded.Groups, Has.Count.EqualTo(1));
            Assert.That(reloaded.Groups.Single(),
                Is.EqualTo(ImmutableHashSet.Create(repairedId, healthy.Id)));
            Assert.That(migratedReference.Id, Is.EqualTo(repairedId));
            Assert.That(migratedReference.Value, Is.SameAs(reloadedRepaired));
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
        Assert.Multiple(() =>
        {
            Assert.That(migratedListReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedListReference.Value, Is.SameAs(migrated));
            Assert.That(migratedDictionaryReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedDictionaryReference.Value, Is.SameAs(migrated));
            Assert.That(migratedReference.Id, Is.EqualTo(migrated.Id));
            Assert.That(migratedReference.Value, Is.SameAs(migrated));
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
    public void StoreToUri_RehomeTarget_NeverOverwritesAnExistingFile()
    {
        (Uri sceneUri, string elementPath) = CreatePersistedScene();
        byte[] corruptBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":["u8.ToArray();
        File.WriteAllBytes(elementPath, corruptBytes);

        Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
        string rehomedPath = Path.Combine(_root, "rehomed", Path.GetFileName(elementPath));
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        byte[] repairedBytes = "{\"Id\":\"85f4d478-e16d-4cb1-ab71-ee1a90a03fe0\",\"Objects\":[]}"u8.ToArray();
        File.WriteAllBytes(rehomedPath, repairedBytes);
        CoreSerializer.StoreToUri(recovered, new Uri(rehomedPath));

        Assert.That(File.ReadAllBytes(rehomedPath), Is.EqualTo(repairedBytes));
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
