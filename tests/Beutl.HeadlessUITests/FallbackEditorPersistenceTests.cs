using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class FallbackEditorPersistenceTests
{
    [SuppressResourceClassGeneration]
    public sealed class EditorValueHolder : EngineObject
    {
        public EditorValueHolder()
        {
            ScanProperties<EditorValueHolder>();
        }

        public IProperty<EngineObject> CoreValue { get; } = Property.CreateAnimatable<EngineObject>();

        public IProperty<Geometry?> GeometryValue { get; } = Property.CreateAnimatable<Geometry?>();

        public IProperty<Brush?> BrushValue { get; } = Property.CreateAnimatable<Brush?>();

        public IProperty<Pen?> PenValue { get; } = Property.Create<Pen?>();

        public IProperty<TextureSource?> TextureValue { get; } = Property.Create<TextureSource?>();
    }

    [SuppressResourceClassGeneration]
    public sealed class NullableRecoveryHolder : EngineObject
    {
        public NullableRecoveryHolder() => ScanProperties<NullableRecoveryHolder>();

        public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

        public override void Deserialize(ICoreSerializationContext context)
        {
            Id = context.GetValue<Guid>(nameof(Id));
            Transform.CurrentValue = context.GetValue<EngineObject>(nameof(Transform)) as DisplacementMapTransform;
        }
    }

    // NullableRecoveryHolder with a property that cannot be animated.
    [SuppressResourceClassGeneration]
    public sealed class NullableRecoveryHolderWithAmount : EngineObject
    {
        public NullableRecoveryHolderWithAmount() => ScanProperties<NullableRecoveryHolderWithAmount>();

        public IProperty<DisplacementMapTransform?> Transform { get; } = Property.Create<DisplacementMapTransform?>();

        public IProperty<float> Amount { get; } = Property.Create<float>();

        public override void Deserialize(ICoreSerializationContext context)
        {
            Id = context.GetValue<Guid>(nameof(Id));
            Transform.CurrentValue = context.GetValue<EngineObject>(nameof(Transform)) as DisplacementMapTransform;
        }
    }

    // A brush whose type is missing does not protect its element: pasting over it is an ordinary edit, and
    // undoing it brings the fallback back, which saves the JSON it was read from.
    [AvaloniaTest]
    public async Task BrushTryPasteJson_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            $"fallback-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(new RectShape
            {
                Fill = { CurrentValue = new SolidColorBrush(Colors.Red) },
            });
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject brushJson = elementJson[nameof(Element.Objects)]!.AsArray()[0]!
                [nameof(Shape.Fill)]!.AsObject();
            brushJson["$type"] = "[Beutl.Engine]Beutl.Media:DoesNotExist";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            Assert.That(recoveredShape.Fill.CurrentValue, Is.InstanceOf<IFallback>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);

            var sequence = new OperationSequenceGenerator();
            using var history = new HistoryManager(recoveredElement, sequence);
            using var observer = new CoreObjectOperationObserver(null, recoveredElement, sequence);
            using IDisposable subscription = history.Subscribe(observer);
            var adapter = new SimplePropertyAdapter<Brush?>(
                (SimpleProperty<Brush?>)recoveredShape.Fill,
                recoveredShape);
            using var viewModel = new BrushEditorViewModel(adapter);
            viewModel.Accept(new Visitor(recoveredElement, history));

            bool pasted = viewModel.TryPasteJson(
                CoreSerializer.SerializeToJsonString(new SolidColorBrush(Colors.Blue)));
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = history.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(pasted, Is.True);
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(recoveredShape.Fill.CurrentValue, Is.InstanceOf<IFallback>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [AvaloniaTest]
    public async Task BrushPresenterClearTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertPresenterTargetClearOverAFallbackIsSaved(PresenterKind.Brush);
    }

    [AvaloniaTest]
    public async Task TransformPresenterClearTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertPresenterTargetClearOverAFallbackIsSaved(PresenterKind.Transform);
    }

    [AvaloniaTest]
    public async Task FilterEffectPresenterClearTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertPresenterTargetClearOverAFallbackIsSaved(PresenterKind.FilterEffect);
    }

    [AvaloniaTest]
    public async Task GenericPresenterClearTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertPresenterTargetClearOverAFallbackIsSaved(PresenterKind.Transform, genericEditor: true);
    }

    [AvaloniaTest]
    public async Task PenWholeValueReplacements_OverAFallbackBrush_AreSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var holder = new EditorValueHolder();
            var pen = new Pen();
            pen.Brush.CurrentValue = new SolidColorBrush(Colors.Red);
            holder.PenValue.CurrentValue = pen;
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(holder);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject brushJson = FindObjectWithProperty(elementJson, nameof(Pen.Brush))!
                [nameof(Pen.Brush)]!.AsObject();
            brushJson["$type"] = "[Beutl.Engine]Beutl.Media:MissingBrush";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredPen = recoveredHolder.PenValue.CurrentValue!;
            Assert.That(recoveredPen.Brush.CurrentValue, Is.InstanceOf<FallbackBrush>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new SimplePropertyAdapter<Pen?>(
                (SimpleProperty<Pen?>)recoveredHolder.PenValue,
                recoveredHolder);
            using var viewModel = new PenEditorViewModel(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            var directPen = new Pen();
            directPen.Brush.CurrentValue = new SolidColorBrush(Colors.Blue);
            viewModel.SetValue(recoveredPen, directPen);
            AssertPenReplacementAndUndo(recoveredElement, recoveredHolder, context.History, elementPath, originalBytes, originalJson);

            bool applied = viewModel.ApplyTemplate(
                ObjectTemplateItem.CreateFromInstance(new Pen(), "Template"));
            Assert.That(applied, Is.True);
            AssertPenReplacementAndUndo(recoveredElement, recoveredHolder, context.History, elementPath, originalBytes, originalJson);

            bool pasted = viewModel.TryPasteJson(CoreSerializer.SerializeToJsonString(new Pen()));
            Assert.That(pasted, Is.True);
            AssertPenReplacementAndUndo(recoveredElement, recoveredHolder, context.History, elementPath, originalBytes, originalJson);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task EasingRepair_ResumesPersistenceAndWritesRepairedSidecar()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            (Uri sceneUri, string elementPath) = CreateAnimatedScene(root);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject keyFrameJson = FindObjectWithProperty(elementJson, nameof(KeyFrame.Easing))!;
            keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var shape = (RectShape)recoveredElement.Objects.Single();
            var animation = (KeyFrameAnimation<float>)shape.Width.Animation!;
            var keyFrame = (KeyFrame<float>)animation.KeyFrames.Single();
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new CorePropertyAdapter<Easing>(KeyFrame.EasingProperty, keyFrame);
            using var viewModel = new ValueEditorViewModel<Easing>(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            viewModel.SetValue(keyFrame.Easing, new SplineEasing());
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(keyFrame.Easing, Is.InstanceOf<SplineEasing>());
                Assert.That(context.History.CanUndo, Is.True);
                Assert.That(File.ReadAllBytes(elementPath), Is.Not.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task RemoveKeyFrame_LastLossyEasingResumesPersistenceInReplacementTransaction()
    {
        await AssertAnimationDiscardResumesPersistence(AnimationDiscardKind.RemoveKeyFrame);
    }

    [AvaloniaTest]
    public async Task RemoveAnimation_LastLossyEasingResumesPersistenceInReplacementTransaction()
    {
        await AssertAnimationDiscardResumesPersistence(AnimationDiscardKind.RemoveAnimation);
    }

    [AvaloniaTest]
    public async Task SetExpression_LastLossyEasingResumesPersistenceInReplacementTransaction()
    {
        await AssertAnimationDiscardResumesPersistence(AnimationDiscardKind.SetExpression);
    }

    // Removing a healthy keyframe leaves the lossy one, so the element stays protected and that removal
    // would be lost on save. Only removing the keyframe that holds the lossy easing repairs it.
    [AvaloniaTest]
    public async Task RemoveKeyFrame_RefusesAHealthyKeyFrameWhileAnotherHoldsTheLossyEasing()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var shape = new RectShape();
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 32 });
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.5), Value = 48 });
            shape.Width.Animation = animation;
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(shape);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            FindObjectWithNonEmptyArrayProperty(elementJson, "KeyFrames")!["KeyFrames"]![1]![nameof(KeyFrame.Easing)]
                = "[Missing.Assembly]Missing.Namespace:MissingEasing";
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Element recoveredElement = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            var recoveredAnimation = (KeyFrameAnimation<float>)recoveredShape.Width.Animation!;
            using var context = new EditorTestContext(recoveredElement);
            using var viewModel = new ValueEditorViewModel<float>(new AnimatablePropertyAdapter<float>(
                (AnimatableProperty<float>)recoveredShape.Width, recoveredShape));
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            viewModel.RemoveKeyFrame(TimeSpan.Zero);
            int countAfterHealthyRemoval = recoveredAnimation.KeyFrames.Count;
            viewModel.RemoveKeyFrame(TimeSpan.FromSeconds(0.5));
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(countAfterHealthyRemoval, Is.EqualTo(2));
                Assert.That(recoveredAnimation.KeyFrames.Single().KeyTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
                Assert.That(File.ReadAllBytes(elementPath), Is.Not.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Loading dropped the unreadable transform, so any replacement repairs the element, including an
    // expression on a property that cannot be animated.
    [AvaloniaTest]
    public async Task SetExpression_OnAPropertyThatCannotBeAnimated_CompletesTheRepair()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var holder = new NullableRecoveryHolderWithAmount();
            holder.Transform.CurrentValue = new DisplacementMapTranslateTransform();
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(holder);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            json["Objects"]![0]!["Transform"]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
            File.WriteAllText(elementPath, json.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredHolder = (NullableRecoveryHolderWithAmount)recovered.Objects.Single();
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var viewModel = new ValueEditorViewModel<float>(new SimplePropertyAdapter<float>(
                (SimpleProperty<float>)recoveredHolder.Amount, recoveredHolder));
            viewModel.Accept(new Visitor(recovered, context.History));

            Assert.That(viewModel.SetExpression("1 + 2", out string? error), Is.True, error);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.SuppressedStorageSource, Is.Null);
                Assert.That(File.ReadAllBytes(elementPath), Is.Not.EqualTo(originalBytes));
                Assert.That(File.ReadAllText(elementPath), Does.Contain("1 + 2"));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task CoreObjectApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        await TestReset.ResetShellAsync();

        var holder = new EditorValueHolder();
        var property = (AnimatableProperty<EngineObject>)holder.CoreValue;
        var propertyValue = new RectShape();
        property.CurrentValue = propertyValue;
        var keyFrame = new KeyFrame<EngineObject> { Value = new RectShape() };
        property.Animation = CreateAnimation(keyFrame);
        using var context = new EditorTestContext(holder);
        var adapter = new AnimatablePropertyAdapter<EngineObject>(property, holder);
        using var viewModel = new CoreObjectEditorViewModel<EngineObject>(adapter);
        viewModel.Accept(new Visitor(context.Element, context.History));
        ((BaseEditorViewModel)viewModel).EditingKeyFrame.Value = keyFrame;

        bool applied = viewModel.ApplyTemplate(
            ObjectTemplateItem.CreateFromInstance(new EllipseShape(), "Ellipse"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(property.CurrentValue, Is.SameAs(propertyValue));
            Assert.That(keyFrame.Value, Is.InstanceOf<EllipseShape>());
        });
    }

    [AvaloniaTest]
    public async Task GeometryApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        await TestReset.ResetShellAsync();

        var holder = new EditorValueHolder();
        var property = (AnimatableProperty<Geometry?>)holder.GeometryValue;
        var propertyValue = new RectGeometry();
        property.CurrentValue = propertyValue;
        var keyFrame = new KeyFrame<Geometry?> { Value = new RectGeometry() };
        property.Animation = CreateAnimation(keyFrame);
        using var context = new EditorTestContext(holder);
        var adapter = new AnimatablePropertyAdapter<Geometry?>(property, holder);
        using var viewModel = new GeometryEditorViewModel(adapter);
        viewModel.Accept(new Visitor(context.Element, context.History));
        ((BaseEditorViewModel)viewModel).EditingKeyFrame.Value = keyFrame;

        bool applied = viewModel.ApplyTemplate(
            ObjectTemplateItem.CreateFromInstance(new EllipseGeometry(), "Ellipse"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(property.CurrentValue, Is.SameAs(propertyValue));
            Assert.That(keyFrame.Value, Is.InstanceOf<EllipseGeometry>());
        });
    }

    [AvaloniaTest]
    public async Task BrushApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        await TestReset.ResetShellAsync();

        var holder = new EditorValueHolder();
        var property = (AnimatableProperty<Brush?>)holder.BrushValue;
        var propertyValue = new SolidColorBrush(Colors.Red);
        property.CurrentValue = propertyValue;
        var keyFrame = new KeyFrame<Brush?> { Value = new SolidColorBrush(Colors.Blue) };
        property.Animation = CreateAnimation(keyFrame);
        using var context = new EditorTestContext(holder);
        var adapter = new AnimatablePropertyAdapter<Brush?>(property, holder);
        using var viewModel = new BrushEditorViewModel(adapter);
        viewModel.Accept(new Visitor(context.Element, context.History));
        viewModel.EditingKeyFrame.Value = keyFrame;

        bool applied = viewModel.ApplyTemplate(
            ObjectTemplateItem.CreateFromInstance(new SolidColorBrush(Colors.Green), "Green"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(property.CurrentValue, Is.SameAs(propertyValue));
            Assert.That(keyFrame.Value, Is.InstanceOf<SolidColorBrush>());
            Assert.That(((SolidColorBrush)keyFrame.Value!).Color.CurrentValue, Is.EqualTo(Colors.Green));
        });
    }

    // A texture source's drawable edits replace only its drawable. While the texture's own size animation
    // holds an unreadable easing, such an edit would not complete the repair, so it is refused.
    [AvaloniaTest]
    public async Task TextureDrawableEdits_CheckTheDrawableTheyReplace()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = new RectShape();
            var widthAnimation = new KeyFrameAnimation<int>();
            widthAnimation.KeyFrames.Add(new KeyFrame<int> { KeyTime = TimeSpan.Zero, Value = 256 });
            texture.TextureWidth.Animation = widthAnimation;
            holder.TextureValue.CurrentValue = texture;
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(holder);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            FindObjectWithProperty(elementJson, nameof(KeyFrame.Easing))![nameof(KeyFrame.Easing)]
                = "[Missing.Assembly]Missing.Namespace:MissingEasing";
            File.WriteAllText(elementPath, elementJson.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredHolder = (EditorValueHolder)recovered.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Drawable? drawableBefore = recoveredTexture.Drawable.CurrentValue;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var viewModel = new TextureSourceEditorViewModel(new SimplePropertyAdapter<TextureSource?>(
                (SimpleProperty<TextureSource?>)recoveredHolder.TextureValue, recoveredHolder));
            viewModel.Accept(new Visitor(recovered, context.History));

            viewModel.SetDrawableType(typeof(EllipseShape));
            viewModel.SetDrawableTarget(new EllipseShape());

            Assert.Multiple(() =>
            {
                Assert.That(recoveredTexture.Drawable.CurrentValue, Is.SameAs(drawableBefore));
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(context.History.CanUndo, Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A generic presenter editor that keeps the presenter replaces only its target. While the presenter's
    // own transform holds an easing that could not be read, pointing it at another target would not complete
    // the repair.
    [AvaloniaTest]
    public async Task PresenterTargetEdit_ChecksTheTargetItReplaces()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = new DrawablePresenter { Transform = { CurrentValue = new TranslateTransform() } };
            holder.TextureValue.CurrentValue = texture;
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(holder);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            AddUnreadableEasing<TranslateTransform>(
                FindObjectWithProperty(elementJson, nameof(DrawableTextureSource.Drawable))![nameof(DrawableTextureSource.Drawable)]!
                    [nameof(Drawable.Transform)]!.AsObject(),
                nameof(TranslateTransform.X));
            File.WriteAllText(elementPath, elementJson.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredHolder = (EditorValueHolder)recovered.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            var presenter = (DrawablePresenter)recoveredTexture.Drawable.CurrentValue!;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var editor = new CoreObjectEditorViewModel<Drawable>(new SimplePropertyAdapter<Drawable>(
                (SimpleProperty<Drawable>)recoveredTexture.Drawable, recoveredTexture));
            editor.Accept(new Visitor(recovered, context.History));

            editor.SetTarget(new EllipseShape());

            Assert.Multiple(() =>
            {
                Assert.That(recoveredTexture.Drawable.CurrentValue, Is.SameAs(presenter));
                Assert.That(presenter.Target.Expression, Is.Null);
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(context.History.CanUndo, Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task TextureDrawableType_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = new RectShape();
            holder.TextureValue.CurrentValue = texture;
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(holder);
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject drawableJson = FindObjectWithProperty(elementJson, nameof(DrawableTextureSource.Drawable))!
                [nameof(DrawableTextureSource.Drawable)]!.AsObject();
            drawableJson["$type"] = "[Beutl.Engine]Beutl.Graphics:MissingDrawable";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new SimplePropertyAdapter<TextureSource?>(
                (SimpleProperty<TextureSource?>)recoveredHolder.TextureValue,
                recoveredHolder);
            using var viewModel = new TextureSourceEditorViewModel(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            viewModel.SetDrawableType(typeof(RectShape));
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task TextureSourceReplacement_OverAFallbackDrawable_IsSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = new RectShape();
            holder.TextureValue.CurrentValue = texture;
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(holder);
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject drawableJson = FindObjectWithProperty(elementJson, nameof(DrawableTextureSource.Drawable))!
                [nameof(DrawableTextureSource.Drawable)]!.AsObject();
            drawableJson["$type"] = "[Beutl.Engine]Beutl.Graphics:MissingDrawable";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new SimplePropertyAdapter<TextureSource?>(
                (SimpleProperty<TextureSource?>)recoveredHolder.TextureValue,
                recoveredHolder);
            using var viewModel = new TextureSourceEditorViewModel(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            viewModel.ChangeToImageTextureSource();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(recoveredHolder.TextureValue.CurrentValue, Is.SameAs(recoveredTexture));
                Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task TextureDrawableTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.CurrentValue = new RectShape();
            holder.TextureValue.CurrentValue = texture;
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(holder);
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject drawableJson = FindObjectWithProperty(elementJson, nameof(DrawableTextureSource.Drawable))!
                [nameof(DrawableTextureSource.Drawable)]!.AsObject();
            drawableJson["$type"] = "[Beutl.Engine]Beutl.Graphics:MissingDrawable";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new SimplePropertyAdapter<TextureSource?>(
                (SimpleProperty<TextureSource?>)recoveredHolder.TextureValue,
                recoveredHolder);
            using var viewModel = new TextureSourceEditorViewModel(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            var target = new RectShape();
            viewModel.SetDrawableTarget(target);
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task ListRemoveItem_OfAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertListClearOfAFallbackIsSaved(initialize: false);
    }

    [AvaloniaTest]
    public async Task ListInitialize_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await AssertListClearOfAFallbackIsSaved(initialize: true);
    }

    private static async Task AssertListClearOfAFallbackIsSaved(bool initialize)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var group = new FilterEffectGroup();
            group.Children.Add(new Blur());
            var shape = new RectShape
            {
                FilterEffect = { CurrentValue = group },
            };
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(shape);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject groupJson = FindObjectWithNonEmptyArrayProperty(
                elementJson,
                nameof(FilterEffectGroup.Children))!;
            groupJson[nameof(FilterEffectGroup.Children)]!.AsArray()[0]!.AsObject()["$type"]
                = "[Beutl.Engine]Beutl.Graphics.Effects:MissingFilterEffect";
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            var recoveredGroup = (FilterEffectGroup)recoveredShape.FilterEffect.CurrentValue!;
            Assert.That(recoveredGroup.Children.Single(), Is.InstanceOf<FallbackFilterEffect>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new EnginePropertyAdapter<ICoreList<FilterEffect>>(
                recoveredGroup.Children,
                recoveredGroup);
            using var viewModel = new ListEditorViewModel<FilterEffect>(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            if (initialize)
                viewModel.Initialize();
            else
                viewModel.RemoveItem(0);
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(recoveredGroup.Children.Single(), Is.InstanceOf<FallbackFilterEffect>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // File-backed editors replace a value through the single-argument SetValue, for example a media
    // source that loading dropped. That replacement repairs the element as well.
    [AvaloniaTest]
    public async Task NullRecoveredTransform_SetValueResumesPersistence()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var holder = new NullableRecoveryHolder();
            holder.Transform.CurrentValue = new DisplacementMapTranslateTransform();
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(holder);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            json["Objects"]![0]!["Transform"]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
            File.WriteAllText(elementPath, json.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredHolder = (NullableRecoveryHolder)recovered.Objects.Single();
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var editor = new CoreObjectEditorViewModel<DisplacementMapTransform>(
                new SimplePropertyAdapter<DisplacementMapTransform>(
                    (SimpleProperty<DisplacementMapTransform>)recoveredHolder.Transform, recoveredHolder));
            editor.Accept(new Visitor(recovered, context.History));

            editor.SetValue(new DisplacementMapTranslateTransform());
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.SuppressedStorageSource, Is.Null);
                Assert.That(((NullableRecoveryHolder)CoreSerializer.RestoreFromUri<Element>(recovered.Uri!)
                    .Objects.Single()).Transform.CurrentValue, Is.InstanceOf<DisplacementMapTranslateTransform>());
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Changing a drawable brush's drawable replaces only the drawable, so it repairs the element only when
    // the drawable holds what could not be loaded (here an easing). When the brush's transform holds it, the
    // change is refused instead of being lost on save.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task DrawableBrushEdits_CheckThePartTheyReplace(bool drawableIsUnreadable)
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var shape = new RectShape();
            shape.Fill.CurrentValue = new DrawableBrush
            {
                Drawable = { CurrentValue = new EllipseShape() },
                Transform = { CurrentValue = new TranslateTransform() },
            };
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(shape);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject brushJson = json["Objects"]![0]![nameof(RectShape.Fill)]!.AsObject();
            if (drawableIsUnreadable)
            {
                AddUnreadableEasing<EllipseShape>(
                    brushJson[nameof(DrawableBrush.Drawable)]!.AsObject(), nameof(EllipseShape.Width));
            }
            else
            {
                AddUnreadableEasing<TranslateTransform>(
                    brushJson[nameof(Brush.Transform)]!.AsObject(), nameof(TranslateTransform.X));
            }

            File.WriteAllText(elementPath, json.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredShape = (RectShape)recovered.Objects.Single();
            var brush = (DrawableBrush)recoveredShape.Fill.CurrentValue!;
            Drawable? drawableBefore = brush.Drawable.CurrentValue;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var editor = new BrushEditorViewModel(
                new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)recoveredShape.Fill, recoveredShape));
            editor.Accept(new Visitor(recovered, context.History));

            if (!drawableIsUnreadable)
            {
                editor.SetDrawableTarget(null);
                Assert.That(brush.Drawable.CurrentValue, Is.SameAs(drawableBefore),
                    "Pointing the kept brush at a target replaces only its drawable.");
            }

            editor.ChangeDrawableType(typeof(RectShape));

            Assert.Multiple(() =>
            {
                if (drawableIsUnreadable)
                {
                    Assert.That(brush.Drawable.CurrentValue, Is.InstanceOf<RectShape>());
                    Assert.That(recovered.SuppressedStorageSource, Is.Null);
                }
                else
                {
                    Assert.That(brush.Drawable.CurrentValue, Is.SameAs(drawableBefore));
                    Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                    Assert.That(context.History.CanUndo, Is.False);
                }
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A replacement that itself holds what could not be loaded (a transform group with a child whose
    // keyframe easing could not be read) never completes the repair, so it is refused instead of being lost
    // on save.
    [AvaloniaTest]
    public async Task SetValue_RefusesAReplacementThatHoldsWhatCouldNotBeLoaded()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var shape = new RectShape();
            shape.Transform.CurrentValue = new TranslateTransform();
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(shape);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            AddUnreadableEasing<TranslateTransform>(
                json["Objects"]![0]![nameof(RectShape.Transform)]!.AsObject(), nameof(TranslateTransform.X));
            File.WriteAllText(elementPath, json.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredShape = (RectShape)recovered.Objects.Single();
            Transform? unavailable = recoveredShape.Transform.CurrentValue;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var editor = new CoreObjectEditorViewModel<Transform>(new SimplePropertyAdapter<Transform>(
                (SimpleProperty<Transform>)recoveredShape.Transform, recoveredShape));
            editor.Accept(new Visitor(recovered, context.History));
            var replacement = new TransformGroup();
            replacement.Children.Add(CreateWithUnreadableEasing<TranslateTransform>(nameof(TranslateTransform.X)));

            editor.SetValue(unavailable, replacement);
            editor.SetValue(replacement);

            Assert.Multiple(() =>
            {
                Assert.That(recoveredShape.Transform.CurrentValue, Is.SameAs(unavailable));
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(context.History.CanUndo, Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Pasting a brush over the one that holds an easing that could not be read goes through the same replacement
    // check: a pasted drawable brush whose transform holds one too would leave the element protected and be lost.
    [AvaloniaTest]
    public async Task BrushTryPasteJson_RefusesABrushThatCannotBeLoadedEither()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(new RectShape { Fill = { CurrentValue = new SolidColorBrush(Colors.Red) } });
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            AddUnreadableEasing<SolidColorBrush>(
                elementJson[nameof(Element.Objects)]![0]![nameof(Shape.Fill)]!.AsObject(), nameof(Brush.Opacity));
            File.WriteAllText(elementPath, elementJson.ToJsonString());

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredShape = (RectShape)recovered.Objects.Single();
            Brush? unavailable = recoveredShape.Fill.CurrentValue;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var context = new EditorTestContext(recovered);
            using var viewModel = new BrushEditorViewModel(new SimplePropertyAdapter<Brush?>(
                (SimpleProperty<Brush?>)recoveredShape.Fill, recoveredShape));
            viewModel.Accept(new Visitor(recovered, context.History));
            JsonObject pastedJson = CoreSerializer.SerializeToJsonObject(new DrawableBrush
            {
                Drawable = { CurrentValue = new EllipseShape() },
                Transform = { CurrentValue = new TranslateTransform() },
            });
            AddUnreadableEasing<TranslateTransform>(pastedJson[nameof(Brush.Transform)]!.AsObject(), nameof(TranslateTransform.X));

            viewModel.TryPasteJson(pastedJson.ToJsonString());

            Assert.Multiple(() =>
            {
                Assert.That(recoveredShape.Fill.CurrentValue, Is.SameAs(unavailable));
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(context.History.CanUndo, Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public async Task NullRecoveredTransform_ChangeTypeResumesPersistenceAndUndoRestoresBytes()
    {
        await TestReset.ResetShellAsync();
        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var effect = new NullableRecoveryHolder();
            effect.Transform.CurrentValue = new DisplacementMapTranslateTransform();
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
            element.AddObject(effect);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            json["Objects"]![0]!["Transform"]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";
            File.WriteAllText(elementPath, json.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Element recovered = CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single();
            var recoveredEffect = (NullableRecoveryHolder)recovered.Objects.Single();
            Assert.That(recoveredEffect.Transform.CurrentValue, Is.Null);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            using var context = new EditorTestContext(recovered);
            using var editor = new CoreObjectEditorViewModel<DisplacementMapTransform>(
                new SimplePropertyAdapter<DisplacementMapTransform>(
                    (SimpleProperty<DisplacementMapTransform>)recoveredEffect.Transform, recoveredEffect));
            editor.Accept(new Visitor(recovered, context.History));

            editor.SetNewInstance(typeof(DisplacementMapTranslateTransform));
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(((NullableRecoveryHolder)CoreSerializer.RestoreFromUri<Element>(recovered.Uri!)
                .Objects.Single()).Transform.CurrentValue, Is.InstanceOf<DisplacementMapTranslateTransform>());
            Assert.That(context.History.Undo(), Is.True);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static KeyFrameAnimation<T> CreateAnimation<T>(KeyFrame<T> keyFrame)
    {
        var animation = new KeyFrameAnimation<T>();
        animation.KeyFrames.Add(keyFrame);
        return animation;
    }

    private static async Task AssertPresenterTargetClearOverAFallbackIsSaved(PresenterKind kind, bool genericEditor = false)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
            string elementPath = Path.Combine(root, "element.belm");
            var shape = new RectShape();
            SetPresenter(shape, kind);
            var element = new Element
            {
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(elementPath),
            };
            element.AddObject(shape);
            var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, sceneUri);

            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject targetJson = FindObjectWithProperty(elementJson, nameof(BrushPresenter.Target))!
                [nameof(BrushPresenter.Target)]!.AsObject();
            targetJson["$type"] = kind switch
            {
                PresenterKind.Brush => "[Beutl.Engine]Beutl.Media:MissingBrush",
                PresenterKind.Transform => "[Beutl.Engine]Beutl.Graphics.Transformation:MissingTransform",
                PresenterKind.FilterEffect => "[Beutl.Engine]Beutl.Graphics.Effects:MissingFilterEffect",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            string originalJson = elementJson.ToJsonString();
            File.WriteAllText(elementPath, originalJson);
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            Assert.That(GetPresenterTarget(recoveredShape, kind), Is.InstanceOf<IFallback>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var context = new EditorTestContext(recoveredElement);
            using BaseEditorViewModel viewModel = genericEditor
                ? new CoreObjectEditorViewModel<Transform>(
                    new SimplePropertyAdapter<Transform>((SimpleProperty<Transform>)recoveredShape.Transform, recoveredShape))
                : CreatePresenterEditor(recoveredShape, kind);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            if (genericEditor)
                ((ICoreObjectEditorViewModel)viewModel).SetTarget(null);
            else
                ClearPresenterTarget(viewModel, kind);
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(GetPresenterTarget(recoveredShape, kind), Is.InstanceOf<IFallback>());
                Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task AssertAnimationDiscardResumesPersistence(AnimationDiscardKind kind)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            (Uri sceneUri, string elementPath) = CreateAnimatedScene(root);
            JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            JsonObject keyFrameJson = FindObjectWithProperty(elementJson, nameof(KeyFrame.Easing))!;
            keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var shape = (RectShape)recoveredElement.Objects.Single();
            var animation = (KeyFrameAnimation<float>)shape.Width.Animation!;
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(1));
            using var context = new EditorTestContext(recoveredElement);
            var adapter = new AnimatablePropertyAdapter<float>(
                (AnimatableProperty<float>)shape.Width,
                shape);
            using var viewModel = new ValueEditorViewModel<float>(adapter);
            viewModel.Accept(new Visitor(recoveredElement, context.History));

            switch (kind)
            {
                case AnimationDiscardKind.RemoveKeyFrame:
                    viewModel.RemoveKeyFrame(TimeSpan.Zero);
                    break;
                case AnimationDiscardKind.RemoveAnimation:
                    viewModel.RemoveAnimation();
                    break;
                case AnimationDiscardKind.SetExpression:
                    Assert.That(viewModel.SetExpression("1 + 2", out string? error), Is.True, error);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            byte[] repairedBytes = File.ReadAllBytes(elementPath);
            bool undone = context.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            var restoredAnimation = (KeyFrameAnimation<float>)shape.Width.Animation!;

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
                Assert.That(undone, Is.True);
                Assert.That(restoredAnimation.KeyFrames, Has.Count.EqualTo(1));
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void SetPresenter(RectShape shape, PresenterKind kind)
    {
        switch (kind)
        {
            case PresenterKind.Brush:
                shape.Fill.CurrentValue = new BrushPresenter
                {
                    Target = { CurrentValue = new SolidColorBrush(Colors.Red) },
                };
                break;
            case PresenterKind.Transform:
                shape.Transform.CurrentValue = new TransformPresenter
                {
                    Target = { CurrentValue = new TranslateTransform() },
                };
                break;
            case PresenterKind.FilterEffect:
                shape.FilterEffect.CurrentValue = new FilterEffectPresenter
                {
                    Target = { CurrentValue = new Blur() },
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static BaseEditorViewModel CreatePresenterEditor(RectShape shape, PresenterKind kind)
    {
        return kind switch
        {
            PresenterKind.Brush => new BrushEditorViewModel(
                new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape)),
            PresenterKind.Transform => new TransformEditorViewModel(
                new SimplePropertyAdapter<Transform?>((SimpleProperty<Transform?>)shape.Transform, shape)),
            PresenterKind.FilterEffect => new FilterEffectEditorViewModel(
                new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static object? GetPresenterTarget(RectShape shape, PresenterKind kind)
    {
        return kind switch
        {
            PresenterKind.Brush => ((BrushPresenter)shape.Fill.CurrentValue!).Target.CurrentValue,
            PresenterKind.Transform => ((TransformPresenter)shape.Transform.CurrentValue!).Target.CurrentValue,
            PresenterKind.FilterEffect => ((FilterEffectPresenter)shape.FilterEffect.CurrentValue!).Target.CurrentValue,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static void ClearPresenterTarget(BaseEditorViewModel viewModel, PresenterKind kind)
    {
        switch (kind)
        {
            case PresenterKind.Brush:
                ((BrushEditorViewModel)viewModel).SetTarget(null);
                break;
            case PresenterKind.Transform:
                ((TransformEditorViewModel)viewModel).SetTarget(null);
                break;
            case PresenterKind.FilterEffect:
                ((FilterEffectEditorViewModel)viewModel).SetTarget(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void AssertPenReplacementAndUndo(
        Element recoveredElement,
        EditorValueHolder recoveredHolder,
        HistoryManager history,
        string elementPath,
        byte[] originalBytes,
        string originalJson)
    {
        CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
        byte[] repairedBytes = File.ReadAllBytes(elementPath);
        bool undone = history.Undo();
        CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(repairedBytes, Is.Not.EqualTo(originalBytes));
            Assert.That(undone, Is.True);
            Assert.That(recoveredHolder.PenValue.CurrentValue!.Brush.CurrentValue, Is.InstanceOf<FallbackBrush>());
            Assert.That(HoldsWhatWasRead(elementPath, originalJson), Is.True);
        });
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            $"fallback-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static (Uri SceneUri, string ElementPath) CreateAnimatedScene(string root)
    {
        var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
        string elementPath = Path.Combine(root, "element.belm");
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 32,
        });
        shape.Width.Animation = animation;
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(elementPath),
        };
        element.AddObject(shape);
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);
        return (sceneUri, elementPath);
    }

    private const string MissingEasingType = "[Missing.Assembly]Missing.Namespace:MissingEasing";

    // Gives the object that objectJson describes an animation of its float property propertyName whose keyframe
    // easing type is missing. Recovery keeps the file of an element that holds one, so the tests of repairs use it
    // now that an object whose type is missing no longer protects its element.
    private static void AddUnreadableEasing<T>(JsonObject objectJson, string propertyName)
        where T : EngineObject, new()
    {
        var obj = new T();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 1 });
        obj.Properties.Single(property => property.Name == propertyName).Animation = animation;
        JsonObject animationJson = CoreSerializer.SerializeToJsonObject(obj)["Animations"]![propertyName]!
            .DeepClone()
            .AsObject();
        animationJson[nameof(KeyFrameAnimation.KeyFrames)]![0]![nameof(KeyFrame.Easing)] = MissingEasingType;
        objectJson["Animations"]![propertyName] = animationJson;
    }

    // A new T whose float property propertyName has a keyframe whose easing could not be read.
    private static T CreateWithUnreadableEasing<T>(string propertyName)
        where T : EngineObject, new()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new T());
        AddUnreadableEasing<T>(json, propertyName);
        return (T)CoreSerializer.DeserializeFromJsonObject(json, typeof(T));
    }

    // Whether the element file holds what it held when it was read: a fallback saves the JSON it was read from,
    // and everything else is saved as it was loaded.
    private static bool HoldsWhatWasRead(string elementPath, string originalJson)
    {
        return JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(elementPath)), JsonNode.Parse(originalJson));
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
                if (child is not null && FindObjectWithProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child is not null && FindObjectWithProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static JsonObject? FindObjectWithNonEmptyArrayProperty(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            if (obj[propertyName] is JsonArray { Count: > 0 })
            {
                return obj;
            }

            foreach ((string _, JsonNode? child) in obj)
            {
                if (child is not null
                    && FindObjectWithNonEmptyArrayProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                if (child is not null
                    && FindObjectWithNonEmptyArrayProperty(child, propertyName) is { } result)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private sealed class EditorTestContext : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;
        private readonly IDisposable _subscription;

        public EditorTestContext(Hierarchical obj)
        {
            Element = obj as Element ?? new Element { Uri = new Uri("file:///editor-test.belm") };
            if (obj is not Beutl.ProjectSystem.Element)
            {
                Element.AddObject((EngineObject)obj);
            }

            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(Element, sequence);
            _observer = new CoreObjectOperationObserver(null, Element, sequence);
            _subscription = History.Subscribe(_observer);
        }

        public Element Element { get; }

        public HistoryManager History { get; }

        public void Dispose()
        {
            _subscription.Dispose();
            _observer.Dispose();
            History.Dispose();
        }
    }

    private sealed record Visitor(Element Element, HistoryManager History)
        : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Element))
                return Element;

            if (serviceType == typeof(HistoryManager))
                return History;

            if (serviceType == typeof(ExtensionProvider))
                return TestShell.Extensions;

            return null;
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }

    private enum PresenterKind
    {
        Brush,
        Transform,
        FilterEffect,
    }

    private enum AnimationDiscardKind
    {
        RemoveKeyFrame,
        RemoveAnimation,
        SetExpression,
    }
}
