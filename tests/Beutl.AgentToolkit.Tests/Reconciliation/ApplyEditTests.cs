using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ApplyEditTests
{
    private sealed class RegisteredTransformElement : Element
    {
        public RegisteredTransformElement()
        {
            ScanProperties<RegisteredTransformElement>();
        }

        public IProperty<Transform?> PluginTransformProperty { get; } = new ValueOnlyProperty<Transform?>(null);

        public Transform? PluginTransform
        {
            get => PluginTransformProperty.Value;
            set => PluginTransformProperty.Value = value;
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class DictionaryTransformHolder : EngineObject
    {
        public DictionaryTransformHolder()
        {
            ScanProperties<DictionaryTransformHolder>();
        }

        public IExpressionProperty<Dictionary<string, Transform>> Transforms { get; }
            = Property.CreateExpression<Dictionary<string, Transform>>();
    }

    [SuppressResourceClassGeneration]
    public sealed class ArbitraryValueHolder : EngineObject
    {
        public ArbitraryValueHolder()
        {
            ScanProperties<ArbitraryValueHolder>();
        }

        public IExpressionProperty<object?> Value { get; } = Property.CreateExpression<object?>();
    }

    [SuppressResourceClassGeneration]
    public sealed class OptionalTransformHolder : EngineObject
    {
        public OptionalTransformHolder()
        {
            ScanProperties<OptionalTransformHolder>();
        }

        public IExpressionProperty<Optional<Transform>> Transform { get; }
            = Property.CreateExpression<Optional<Transform>>();
    }

    public sealed record TransformWrapper(ICoreSerializable Transform);

    [SuppressResourceClassGeneration]
    public sealed class WrappedTransformHolder : EngineObject
    {
        public WrappedTransformHolder() => ScanProperties<WrappedTransformHolder>();

        public IExpressionProperty<List<TransformWrapper>> Wrapped { get; } = Property.CreateAnimatable<List<TransformWrapper>>();
    }

    [Test]
    public void Apply_edit_rejects_new_fallback_inside_record_wrapper()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var holder = new WrappedTransformHolder();
        var group = new TransformGroup();
        group.Children.Add(new RotationTransform());
        var original = new List<TransformWrapper> { new(group) };
        holder.Wrapped.Value = original;
        element.AddObject(holder);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject desired = session.Documents.Read(scene);
        JsonObject holderJson = desired["Elements"]![0]!["Objects"]!.AsArray()
            .OfType<JsonObject>().Single(obj => obj["Id"]!.GetValue<string>() == holder.Id.ToString());
        holderJson["Wrapped"]![0]!["Transform"]!["Children"]![0]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
        holderJson["Wrapped"]![0]!["Transform"]!["Children"]![0]!.AsObject().Remove("Id");

        var result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain("fallback object"));
            Assert.That(holder.Wrapped.Value, Is.SameAs(original));
        });
    }

    [SuppressResourceClassGeneration]
    public sealed class VariableFailureTransform : Transform
    {
        public override Matrix CreateMatrix(Beutl.Composition.CompositionContext context) => Matrix.Identity;
        public override void Deserialize(ICoreSerializationContext context)
            => throw new InvalidOperationException($"Plugin failed at {Guid.NewGuid()}");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Apply_edit_preserves_existing_fallback_inside_record_wrapper(bool variableMessage)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var fallbackJson = new JsonObject
        {
            ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform",
            ["Id"] = Guid.NewGuid().ToString(),
        };
        if (variableMessage) fallbackJson.WriteDiscriminator(typeof(VariableFailureTransform));
        var fallback = (Transform)CoreSerializer.DeserializeFromJsonObject(fallbackJson, typeof(Transform));
        var group = new TransformGroup();
        group.Children.Add(fallback);
        var holder = new WrappedTransformHolder();
        holder.Wrapped.Value = [new(group)];
        element.AddObject(holder);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject desired = session.Documents.Read(scene);
        desired["Elements"]![0]!["Start"] = TimeSpan.FromSeconds(2).ToString("c");

        var result = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(element.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(((TransformGroup)holder.Wrapped.Value![0].Transform).Children.Single(), Is.InstanceOf<IFallback>());
        });
    }

    [Test]
    public void Apply_edit_rejects_duplicate_hidden_fallback_when_animation_is_unchanged()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var holder = new WrappedTransformHolder();
        holder.Wrapped.Value = [];
        var animation = new KeyFrameAnimation<List<TransformWrapper>>();
        animation.KeyFrames.Add(new KeyFrame<List<TransformWrapper>>
        {
            Value = [new(new PlainFallback())],
        });
        holder.Wrapped.SetKeyFrameAnimation(animation);
        element.AddObject(holder);
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        JsonObject holderJson = desired["Elements"]![0]!["Objects"]!.AsArray()
            .OfType<JsonObject>().Single(obj => obj["Id"]!.GetValue<string>() == holder.Id.ToString());
        holderJson["Wrapped"] = holderJson["Animations"]!["Wrapped"]!["KeyFrames"]![0]!["Value"]!.DeepClone();

        Assert.That(() => new Reconciler().PlanFromCurrent(session, _ => (desired, null)), Throws.TypeOf<ReconcileException>());
        Assert.That(holder.Wrapped.Value, Is.Empty);
    }

    [Test]
    public void Apply_edit_rejects_lossy_easing_inside_record_wrapper()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var holder = new WrappedTransformHolder();
        holder.Wrapped.Value = [new(new KeyFrame<float>())];
        element.AddObject(holder);
        using var session = new AgentToolkitTestSession(scene);
        JsonObject desired = session.Documents.Read(scene);
        JsonObject holderJson = desired["Elements"]![0]!["Objects"]!.AsArray()
            .OfType<JsonObject>().Single(obj => obj["Id"]!.GetValue<string>() == holder.Id.ToString());
        holderJson["Wrapped"]![0]!["Transform"]!["Easing"] = "[Missing.Plugin]Missing.Namespace:MissingEasing";

        Assert.That(() => new Reconciler().PlanFromCurrent(session, _ => (desired, null)), Throws.TypeOf<ReconcileException>());
        Assert.That(((KeyFrame<float>)holder.Wrapped.Value![0].Transform).Easing,
            Is.InstanceOf<Beutl.Animation.Easings.LinearEasing>());
    }

    public sealed class PlainFallback : IFallback
    {
        public JsonObject? Json { get; set; } = new()
        {
            ["$type"] = "[Missing.Plugin]Missing.Namespace:MissingValue",
        };

        public FallbackReason Reason { get; set; } = FallbackReason.TypeNotFound;

        public string? ErrorMessage { get; set; }

        public void Serialize(ICoreSerializationContext context)
        {
            (context as IJsonSerializationContext)?.SetJsonObject(Json!);
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            Json = (context as IJsonSerializationContext)?.GetJsonObject();
        }

        public bool TryGetTypeName([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? result)
        {
            result = "[Missing.Plugin]Missing.Namespace:MissingValue";
            return true;
        }
    }

    [Test]
    public void Apply_edit_applies_patch_directly()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Start)] = TimeSpan.FromSeconds(3).ToString("c")
            })
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(apply.Value.AppliedChangeSet, Has.Count.EqualTo(apply.Value.Changes!.Count));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [Test]
    public void Apply_edit_rejects_an_inserted_element_with_a_negative_start_without_mutation()
    {
        Scene scene = TestWorkspace.CreateScene();
        TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        int originalCount = scene.Children.Count;

        JsonObject desired = session.Documents.Read(scene);
        ((JsonArray)desired["Elements"]!).Add(new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Element",
            [nameof(CoreObject.Name)] = "Negative start element",
            [nameof(Element.Start)] = TimeSpan.FromSeconds(-1).ToString("c"),
            [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c")
        });

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(scene.Children, Has.Count.EqualTo(originalCount));
        });
    }

    [Test]
    public void Apply_edit_applies_desired_document_directly()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject desired = session.Documents.Read(session.Root);
        ((JsonObject)((JsonArray)desired["Elements"]!)[0]!)[nameof(Element.Start)] = TimeSpan.FromSeconds(5).ToString("c");

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(scene.Children.Single().Id, Is.EqualTo(element.Id));
            Assert.That(scene.Children.Single().Start, Is.EqualTo(MediaTime.FromSeconds(5)));
        });
    }

    [Test]
    public void Apply_edit_rejects_payloads_that_deserialize_to_fallback_objects_without_mutation()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                [nameof(CoreObject.Name)] = "Invalid rect element",
                [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape)),
                    [nameof(CoreObject.Name)] = "Invalid rect",
                    [nameof(RectShape.Width)] = "not-a-number"
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain("fallback object"));
            Assert.That(apply.Error.Target, Does.Contain("Objects[0]"));
            Assert.That(apply.Error.Hint, Does.Contain("get_schema"));
            Assert.That(apply.Error.Hint, Does.Contain("Objects require concrete EngineObject discriminators"));
            Assert.That(scene.Children, Is.Empty);
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_dictionary_values()
    {
        Scene current = TestWorkspace.CreateScene();
        Element currentElement = TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var currentHolder = new DictionaryTransformHolder();
        var currentTransform = new TranslateTransform(10, 20);
        currentHolder.Transforms.Value = new Dictionary<string, Transform>
        {
            ["move"] = currentTransform,
        };
        currentElement.AddObject(currentHolder);
        using var session = new AgentToolkitTestSession(current);

        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var sandboxHolder = new DictionaryTransformHolder();
        sandboxHolder.Transforms.Value = new Dictionary<string, Transform>
        {
            ["move"] = new FallbackTransform(),
        };
        sandboxElement.AddObject(sandboxHolder);
        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Error.Message, Does.Contain("fallback object"));
            Assert.That(error.Error.Target, Does.Contain(nameof(DictionaryTransformHolder.Transforms)));
            Assert.That(currentHolder.Transforms.Value!["move"], Is.SameAs(currentTransform));
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_optional_values()
    {
        Scene current = TestWorkspace.CreateScene();
        Element currentElement = TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var currentHolder = new OptionalTransformHolder();
        currentHolder.Transform.Value = new Optional<Transform>(new RotationTransform());
        currentElement.AddObject(currentHolder);
        using var session = new AgentToolkitTestSession(current);

        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var sandboxHolder = new OptionalTransformHolder();
        sandboxHolder.Transform.Value = new Optional<Transform>(new FallbackTransform());
        sandboxElement.AddObject(sandboxHolder);
        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Error.Target, Does.Contain(nameof(OptionalTransformHolder.Transform)));
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_element_hierarchy_children_outside_objects()
    {
        Scene current = TestWorkspace.CreateScene();
        TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(current);
        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        ((IModifiableHierarchical)sandboxElement).AddChild(new FallbackTransform());
        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.Multiple(() =>
        {
            Assert.That(sandboxElement.Objects, Is.Empty);
            Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Error.Message, Does.Contain("fallback object"));
            Assert.That(error.Error.Target, Does.Contain("HierarchicalChildren"));
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_registered_properties_on_non_engine_objects()
    {
        Scene current = TestWorkspace.CreateScene();
        current.Children.Add(new RegisteredTransformElement
        {
            Length = MediaTime.FromSeconds(1),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(current.Uri!.LocalPath)!, "element.belm")),
            PluginTransform = new RotationTransform(),
        });
        using var session = new AgentToolkitTestSession(current);
        Scene sandbox = TestWorkspace.CreateScene();
        sandbox.Children.Add(new RegisteredTransformElement
        {
            Length = MediaTime.FromSeconds(1),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(sandbox.Uri!.LocalPath)!, "element.belm")),
            PluginTransform = new FallbackTransform(),
        });
        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Error.Target, Does.Contain(nameof(RegisteredTransformElement.PluginTransform)));
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_non_core_fallbacks()
    {
        Scene current = TestWorkspace.CreateScene();
        TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(current);
        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var holder = new ArbitraryValueHolder();
        holder.Value.Value = new PlainFallback();
        sandboxElement.AddObject(holder);
        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Error.Target, Does.Contain(nameof(ArbitraryValueHolder.Value)));
        });
    }

    [Test]
    public void Validate_no_new_fallback_objects_allows_existing_non_core_fallbacks()
    {
        Scene current = TestWorkspace.CreateScene();
        Element currentElement = TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var currentHolder = new ArbitraryValueHolder();
        currentHolder.Value.Value = new PlainFallback();
        currentElement.AddObject(currentHolder);
        using var session = new AgentToolkitTestSession(current);
        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var sandboxHolder = new ArbitraryValueHolder();
        sandboxHolder.Value.Value = new PlainFallback();
        sandboxElement.AddObject(sandboxHolder);
        Assert.DoesNotThrow(() => Reconciler.ValidateNoNewFallbackObjects(session, sandbox));
    }

    [Test]
    public void Validate_no_new_fallback_objects_rejects_additional_matching_non_core_fallbacks()
    {
        Scene current = TestWorkspace.CreateScene();
        Element currentElement = TestWorkspace.AddElement(current, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var currentHolder = new ArbitraryValueHolder();
        currentHolder.Value.Value = new PlainFallback();
        currentElement.AddObject(currentHolder);
        using var session = new AgentToolkitTestSession(current);
        Scene sandbox = TestWorkspace.CreateScene();
        Element sandboxElement = TestWorkspace.AddElement(sandbox, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        for (int i = 0; i < 2; i++)
        {
            var holder = new ArbitraryValueHolder();
            holder.Value.Value = new PlainFallback();
            sandboxElement.AddObject(holder);
        }

        ReconcileException error = Assert.Throws<ReconcileException>(() =>
            Reconciler.ValidateNoNewFallbackObjects(session, sandbox))!;

        Assert.That(error.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
    }

    [Test]
    public void Apply_edit_returns_compact_response_and_optional_document()
    {
        Scene compactScene = TestWorkspace.CreateScene();
        using var compactSession = new AgentToolkitTestSession(compactScene);
        EditTools compactTools = compactSession.CreateEditTools();

        ToolResult<ApplyEditResponse> compact = compactTools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current);

        Scene documentScene = TestWorkspace.CreateScene();
        using var documentSession = new AgentToolkitTestSession(documentScene);
        EditTools documentTools = documentSession.CreateEditTools();

        ToolResult<ApplyEditResponse> withDocument = documentTools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current,
            includeDocument: true);

        Assert.Multiple(() =>
        {
            Assert.That(compact.IsSuccess, Is.True, compact.Error?.Message);
            Assert.That(compact.Value!.Document, Is.Null);
            Assert.That(compact.Value.Valid, Is.True);
            Assert.That(compact.Value.AppliedChangeSet, Has.Count.EqualTo(compact.Value.Changes!.Count));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted element"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted title"));
            Assert.That(compact.Value.CreatedIds.Select(item => item.Path), Has.Some.Contains("/Elements"));
            Assert.That(withDocument.IsSuccess, Is.True, withDocument.Error?.Message);
            Assert.That(withDocument.Value!.Document, Is.Not.Null);
            Assert.That(withDocument.Value.Document!.ToJsonString(), Does.Contain("Inserted title"));
        });
    }

    [Test]
    public void Apply_edit_quiet_response_returns_summary_without_details()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(
            patch: CreateInsertedTextElementPatch(),
            schemaVersion: SchemaVersion.Current,
            quiet: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Valid, Is.True);
            Assert.That(result.Value.ChangeCount, Is.GreaterThan(0));
            Assert.That(result.Value.Operations.Values.Sum(), Is.EqualTo(result.Value.ChangeCount));
            Assert.That(result.Value.CreatedIds.Select(item => item.Name), Does.Contain("Inserted title"));
            Assert.That(result.Value.Changes, Is.Null);
            Assert.That(result.Value.Validation, Is.Null);
            Assert.That(result.Value.AppliedChangeSet, Is.Null);
        });
    }

    [Test]
    public void Apply_edit_null_filter_effect_clears_then_replacing_does_not_append_children()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var rect = new RectShape
        {
            Name = "Filtered rect",
            Width = { Value = 200 },
            Height = { Value = 100 },
            FilterEffect =
            {
                Value = new FilterEffectGroup
                {
                    Children = { new Blur() }
                }
            }
        };
        element.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> clear = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = null
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);
        bool clearedFilterEffect = rect.FilterEffect.Value is null;

        ToolResult<ApplyEditResponse> replace = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = new JsonObject
                        {
                            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(FilterEffectGroup)),
                            [nameof(FilterEffectGroup.Children)] = new JsonArray(new JsonObject
                            {
                                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Brightness)),
                                [nameof(Brightness.Amount)] = 120
                            })
                        }
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        var effects = (FilterEffectGroup)rect.FilterEffect.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(clear.IsSuccess, Is.True, clear.Error?.Message);
            Assert.That(clear.Value!.Valid, Is.True);
            Assert.That(clearedFilterEffect, Is.True);
            Assert.That(replace.IsSuccess, Is.True, replace.Error?.Message);
            Assert.That(effects.Children, Has.Count.EqualTo(1));
            Assert.That(effects.Children[0], Is.InstanceOf<Brightness>());
        });
    }

    [Test]
    public void Apply_edit_replace_sentinel_swaps_effect_children_and_keeps_group_id()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var group = new FilterEffectGroup
        {
            Children = { new Blur(), new Blur(), new Blur() }
        };
        Guid groupId = group.Id;
        var rect = new RectShape
        {
            Name = "Filtered rect",
            Width = { Value = 200 },
            Height = { Value = 100 },
            FilterEffect = { Value = group }
        };
        element.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> replace = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = new JsonObject
                        {
                            [nameof(CoreObject.Id)] = groupId.ToString(),
                            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(FilterEffectGroup)),
                            [nameof(FilterEffectGroup.Children)] = new JsonArray(
                                new JsonObject { ["$replace"] = true },
                                new JsonObject
                                {
                                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Brightness)),
                                    [nameof(Brightness.Amount)] = 120
                                })
                        }
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        var effects = (FilterEffectGroup)rect.FilterEffect.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(replace.IsSuccess, Is.True, replace.Error?.Message);
            Assert.That(effects.Id, Is.EqualTo(groupId));
            Assert.That(effects.Children, Has.Count.EqualTo(1));
            Assert.That(effects.Children[0], Is.InstanceOf<Brightness>());
        });
    }

    [Test]
    public void Apply_edit_accepts_enum_member_names()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var text = new TextBlock { Text = { Value = "Enum title" } };
        element.AddObject(text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = text.Id.ToString(),
                    [nameof(TextBlock.BlendMode)] = "Plus",
                    [nameof(TextBlock.FontWeight)] = "bold"
                })
            })
        };

        ToolResult<ApplyEditResponse> namedApply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        BlendMode namedBlendMode = text.BlendMode.Value;
        FontWeight namedFontWeight = text.FontWeight.Value;

        Assert.Multiple(() =>
        {
            Assert.That(namedApply.IsSuccess, Is.True, namedApply.Error?.Message);
            Assert.That(namedApply.Value!.Valid, Is.True);
            Assert.That(namedBlendMode, Is.EqualTo(BlendMode.Plus));
            Assert.That(namedFontWeight, Is.EqualTo(FontWeight.Bold));
        });
    }

    private static readonly TestCaseData[] s_invalidEnumValues =
    [
        new TestCaseData(((int)BlendMode.Screen).ToString()).SetName("Apply_edit_rejects_numeric_string_enum_values"),
        new TestCaseData("NotARealMode").SetName("Apply_edit_rejects_unknown_enum_member_names"),
    ];

    [TestCaseSource(nameof(s_invalidEnumValues))]
    public void Apply_edit_rejects_invalid_enum_values(string blendMode)
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var text = new TextBlock { Text = { Value = "Enum title" } };
        element.AddObject(text);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = text.Id.ToString(),
                    [nameof(TextBlock.BlendMode)] = blendMode
                })
            })
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Message, Does.Contain(nameof(TextBlock.BlendMode)));
            Assert.That(text.BlendMode.Value, Is.EqualTo(BlendMode.SrcOver));
        });
    }

    [Test]
    public void Composition_patch_plans_and_applies_through_declarative_loop()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        var inputProps = new JsonObject { ["title"] = "APPLY PATCH" };

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "kinetic-ribbon-title",
            inputProps: inputProps,
            seed: "apply-seed");
        ToolResult<ApplyCompositionResponse> apply = tools.ApplyComposition(
            planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.Plan.Valid, Is.True);
            Assert.That(plan.Value.PlanId, Is.Not.Empty);
            Assert.That(plan.Value.DetailedPlan, Is.Null);
            Assert.That(plan.Value.Composition.Name, Is.EqualTo("kinetic-ribbon-title"));
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.AppliedPlanId, Is.EqualTo(plan.Value.PlanId));
            Assert.That(apply.Value!.Composition.ResolvedProps["title"]!.GetValue<string>(), Is.EqualTo("APPLY PATCH"));
            Assert.That(scene.Children, Has.Count.GreaterThan(3));
            Assert.That(scene.Children.Select(element => element.Name), Does.Contain("Kinetic ribbon title"));
            Assert.That(apply.Value.Result.Document.ToJsonString(), Does.Contain("APPLY PATCH"));
        });
    }

    [Test]
    public void Composition_plan_can_include_detailed_expected_change_set_when_requested()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "kinetic-ribbon-title",
            seed: "detailed-plan-seed",
            includeDetailedPlan: true);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.DetailedPlan, Is.Not.Null);
            Assert.That(plan.Value.DetailedPlan!.ExpectedChangeSet, Has.Count.EqualTo(plan.Value.Plan.ChangeCount));
            Assert.That(plan.Value.Plan.UsageHint, Does.Contain("planId"));
        });
    }

    [Test]
    public void Composition_patch_plans_and_applies_after_existing_elements()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element existingElement = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<PlanCompositionResponse> plan = tools.PlanComposition(
            name: "glitch-cutout-collage",
            inputProps: new JsonObject { ["title"] = "COMPOSITION PROBE" },
            seed: "existing-scene-seed");
        ToolResult<ApplyCompositionResponse> apply = tools.ApplyComposition(planId: plan.Value!.PlanId);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(plan.Value!.Plan.Valid, Is.True);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(scene.Children.Select(element => element.Id), Does.Contain(existingElement.Id));
            Assert.That(scene.Children.Select(element => element.Name), Does.Contain("Glitch title"));
            Assert.That(apply.Value!.Result.Document.ToJsonString(), Does.Contain("COMPOSITION PROBE"));
        });
    }

    [Test]
    public void Apply_edit_rejects_a_flow_operator_inserted_without_a_preceding_portal_object()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        JsonObject bareGroup = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(DrawableGroup)),
                    ["Name"] = "bare group"
                })
            })
        };

        ToolResult<ApplyEditResponse> rejected = tools.ApplyEdit(patch: bareGroup, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Message, Does.Contain("PortalObject"));
            Assert.That(element.Objects, Is.Empty);
        });

        JsonObject pairedGroup = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.ToString(),
                ["Objects"] = new JsonArray(
                    new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(PortalObject))
                    },
                    new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(DrawableGroup)),
                        ["Name"] = "paired group"
                    })
            })
        };

        ToolResult<ApplyEditResponse> accepted = tools.ApplyEdit(patch: pairedGroup, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(accepted.IsSuccess, Is.True, accepted.Error?.Message);
            Assert.That(element.Objects, Has.Count.EqualTo(2));
            Assert.That(element.Objects[0], Is.InstanceOf<PortalObject>());
            Assert.That(element.Objects[1], Is.InstanceOf<DrawableGroup>());
        });
    }

    [Test]
    public void Apply_edit_rejects_a_non_object_identity_array_entry_instead_of_deleting_siblings()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        element.AddObject(new RectShape { Name = "keep" });
        Guid keptId = element.Objects[0].Id;

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        // A null entry alongside a kept Id must be rejected — silently skipping it would let the
        // removal pass delete the kept object.
        JsonObject malformed = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.ToString(),
                ["Objects"] = new JsonArray(null, new JsonObject { ["Id"] = keptId.ToString() })
            })
        };

        ToolResult<ApplyEditResponse> rejected = tools.ApplyEdit(desired: malformed, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(element.Objects[0].Id, Is.EqualTo(keptId));
        });
    }

    [Test]
    public void Apply_edit_rejects_reordering_a_flow_operator_before_its_portal()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var portal = new PortalObject();
        var group = new DrawableGroup { Name = "group" };
        element.Objects.Add(portal);
        element.Objects.Add(group);

        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        // Reorder the valid [Portal, Group] pair into [Group, Portal] — the group now renders with
        // no preceding portal, which the final-order validation must reject.
        JsonObject swap = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = element.Id.ToString(),
                ["Objects"] = new JsonArray(
                    new JsonObject { ["Id"] = group.Id.ToString(), ["$index"] = 0 })
            })
        };

        ToolResult<ApplyEditResponse> rejected = tools.ApplyEdit(patch: swap, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Message, Does.Contain("PortalObject"));
            // Rejected before commit: the original valid order is preserved.
            Assert.That(element.Objects[0], Is.SameAs(portal));
            Assert.That(element.Objects[1], Is.SameAs(group));
        });
    }

    [Test]
    public void Apply_edit_rejects_a_desired_document_whose_schema_version_is_a_number()
    {
        Scene scene = TestWorkspace.CreateScene();
        TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject desired = session.Documents.Read(scene);
        desired[SchemaVersion.PropertyName] = 1;

        ToolResult<ApplyEditResponse> rejected = tools.ApplyEdit(desired: desired);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.SchemaVersionMismatch));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    private static JsonObject CreateInsertedTextElementPatch()
    {
        var element = new Element
        {
            Name = "Inserted element",
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(2)
        };
        element.AddObject(new TextBlock
        {
            Name = "Inserted title",
            Text = { Value = "Inserted title" }
        });

        JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
        CollectionReconciler.RemoveIds(elementJson);
        return new JsonObject
        {
            ["Elements"] = new JsonArray(elementJson)
        };
    }
}
