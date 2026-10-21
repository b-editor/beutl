using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
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
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using static Beutl.Testing.Headless.JsonTestHelpers;

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

        public IExpressionProperty<EngineObject> CoreValue { get; } = Property.CreateAnimatable<EngineObject>();

        public IExpressionProperty<Geometry?> GeometryValue { get; } = Property.CreateAnimatable<Geometry?>();

        public IExpressionProperty<Brush?> BrushValue { get; } = Property.CreateAnimatable<Brush?>();

        public IExpressionProperty<Pen?> PenValue { get; } = Property.CreateWithExpression<Pen?>();

        public IExpressionProperty<TextureSource?> TextureValue { get; } = Property.CreateWithExpression<TextureSource?>();
    }

    [SuppressResourceClassGeneration]
    public sealed class NullableRecoveryHolder : EngineObject
    {
        public NullableRecoveryHolder() => ScanProperties<NullableRecoveryHolder>();

        public IExpressionProperty<DisplacementMapTransform?> Transform { get; } = Property.CreateWithExpression<DisplacementMapTransform?>();

        public IExpressionProperty<float> Amount { get; } = Property.CreateWithExpression<float>();

        public override void Deserialize(ICoreSerializationContext context)
        {
            Id = context.GetValue<Guid>(nameof(Id));
            Transform.Value = context.GetValue<EngineObject>(nameof(Transform)) as DisplacementMapTransform;
        }
    }

    // A brush whose type is missing does not protect its element: pasting over it is an ordinary edit, and
    // undoing it brings the fallback back, which saves the JSON it was read from.
    [AvaloniaTest]
    public async Task BrushTryPasteJson_OverAFallback_IsSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            Recovered recovered = StoreCorruptAndRestore(
                root,
                new RectShape { Fill = { Value = new SolidColorBrush(Colors.Red) } },
                json => json[nameof(Element.Objects)]!.AsArray()[0]![nameof(Shape.Fill)]!.AsObject()["$type"]
                    = "[Beutl.Engine]Beutl.Media:DoesNotExist");
            Element recoveredElement = recovered.Element;
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            Assert.That(recoveredShape.Fill.Value, Is.InstanceOf<IFallback>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);

            using var history = new HistoryScope(recoveredElement);
            var adapter = new SimplePropertyAdapter<Brush?>(
                (SimpleProperty<Brush?>)recoveredShape.Fill,
                recoveredShape);
            using var viewModel = new BrushEditorViewModel(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

            bool pasted = viewModel.TryPasteJson(
                CoreSerializer.SerializeToJsonString(new SolidColorBrush(Colors.Blue)));

            Assert.That(pasted, Is.True);
            AssertRepairThenUndoRestoresBytes(recovered, history.History,
                () => Assert.That(recoveredShape.Fill.Value, Is.InstanceOf<IFallback>()));
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
    public async Task PenWholeValueReplacements_OverAFallbackBrush_AreSavedAndUndoWritesTheFallbackBack()
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var holder = new EditorValueHolder();
            var pen = new Pen();
            pen.Brush.Value = new SolidColorBrush(Colors.Red);
            holder.PenValue.Value = pen;
            Recovered recovered = StoreCorruptAndRestore(
                root,
                holder,
                json => FindObject(json, obj => obj.ContainsKey(nameof(Pen.Brush)))![nameof(Pen.Brush)]!.AsObject()["$type"]
                    = "[Beutl.Engine]Beutl.Media:MissingBrush");
            Element recoveredElement = recovered.Element;
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredPen = recoveredHolder.PenValue.Value!;
            Assert.That(recoveredPen.Brush.Value, Is.InstanceOf<FallbackBrush>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var history = new HistoryScope(recoveredElement);
            var adapter = new SimplePropertyAdapter<Pen?>(
                (SimpleProperty<Pen?>)recoveredHolder.PenValue,
                recoveredHolder);
            using var viewModel = new PenEditorViewModel(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

            var directPen = new Pen();
            directPen.Brush.Value = new SolidColorBrush(Colors.Blue);
            viewModel.SetValue(recoveredPen, directPen);
            AssertFallbackBrushRestored();

            bool applied = viewModel.ApplyTemplate(
                ObjectTemplateItem.CreateFromInstance(new Pen(), "Template"));
            Assert.That(applied, Is.True);
            AssertFallbackBrushRestored();

            bool pasted = viewModel.TryPasteJson(CoreSerializer.SerializeToJsonString(new Pen()));
            Assert.That(pasted, Is.True);
            AssertFallbackBrushRestored();

            void AssertFallbackBrushRestored() => AssertRepairThenUndoRestoresBytes(recovered, history.History,
                () => Assert.That(recoveredHolder.PenValue.Value!.Brush.Value, Is.InstanceOf<FallbackBrush>()));
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
            Recovered recovered = StoreCorruptAndRestore(root, CreateAnimatedShape(), CorruptTheEasing);
            Element recoveredElement = recovered.Element;
            var shape = (RectShape)recoveredElement.Objects.Single();
            var animation = (KeyFrameAnimation<float>)shape.Width.GetKeyFrameAnimation()!;
            var keyFrame = (KeyFrame<float>)animation.KeyFrames.Single();
            using var history = new HistoryScope(recoveredElement);
            var adapter = new EnginePropertyAdapter<Easing>(keyFrame.EasingProperty, keyFrame);
            using var viewModel = new ValueEditorViewModel<Easing>(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

            viewModel.SetValue(keyFrame.Easing, new SplineEasing());
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(keyFrame.Easing, Is.InstanceOf<SplineEasing>());
                Assert.That(history.History.CanUndo, Is.True);
                Assert.That(File.ReadAllBytes(recovered.ElementPath), Is.Not.EqualTo(recovered.OriginalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
            var shape = new RectShape();
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 32 });
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.5), Value = 48 });
            shape.Width.SetKeyFrameAnimation(animation);
            Recovered recovered = StoreCorruptAndRestore(
                root,
                shape,
                json => FindObject(json, obj => obj["KeyFrames"] is JsonArray { Count: > 0 })!["KeyFrames"]![1]!
                    [nameof(KeyFrame.Easing)] = MissingEasingType);

            Element recoveredElement = recovered.Element;
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            var recoveredAnimation = (KeyFrameAnimation<float>)recoveredShape.Width.GetKeyFrameAnimation()!;
            using var history = new HistoryScope(recoveredElement);
            using var viewModel = new ValueEditorViewModel<float>(new AnimatablePropertyAdapter<float>(
                (AnimatableProperty<float>)recoveredShape.Width, recoveredShape));
            viewModel.Accept(Services(recoveredElement, history.History));

            viewModel.RemoveKeyFrame(TimeSpan.Zero);
            int countAfterHealthyRemoval = recoveredAnimation.KeyFrames.Count;
            viewModel.RemoveKeyFrame(TimeSpan.FromSeconds(0.5));
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(countAfterHealthyRemoval, Is.EqualTo(2));
                Assert.That(recoveredAnimation.KeyFrames.Single().KeyTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
                Assert.That(File.ReadAllBytes(recovered.ElementPath), Is.Not.EqualTo(recovered.OriginalBytes));
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
            Recovered stored = StoreCorruptAndRestore(root, CreateTransformHolder(), CorruptTheTransform);
            Element recovered = stored.Element;
            var recoveredHolder = (NullableRecoveryHolder)recovered.Objects.Single();
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var viewModel = new ValueEditorViewModel<float>(new SimplePropertyAdapter<float>(
                (SimpleProperty<float>)recoveredHolder.Amount, recoveredHolder));
            viewModel.Accept(Services(recovered, history.History));

            Assert.That(viewModel.SetExpression("1 + 2", out string? error), Is.True, error);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.SuppressedStorageSource, Is.Null);
                Assert.That(File.ReadAllBytes(stored.ElementPath), Is.Not.EqualTo(stored.OriginalBytes));
                Assert.That(File.ReadAllText(stored.ElementPath), Does.Contain("1 + 2"));
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
        await AssertApplyTemplateUpdatesOnlyTheEditingKeyFrame<EngineObject>(
            holder => holder.CoreValue, new RectShape(), new RectShape(),
            adapter => new CoreObjectEditorViewModel<EngineObject>(adapter), new EllipseShape(), "Ellipse");
    }

    [AvaloniaTest]
    public async Task GeometryApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        await AssertApplyTemplateUpdatesOnlyTheEditingKeyFrame<Geometry?>(
            holder => holder.GeometryValue, new RectGeometry(), new RectGeometry(),
            adapter => new GeometryEditorViewModel(adapter), new EllipseGeometry(), "Ellipse");
    }

    [AvaloniaTest]
    public async Task BrushApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        KeyFrame<Brush?> keyFrame = await AssertApplyTemplateUpdatesOnlyTheEditingKeyFrame<Brush?>(
            holder => holder.BrushValue, new SolidColorBrush(Colors.Red), new SolidColorBrush(Colors.Blue),
            adapter => new BrushEditorViewModel(adapter), new SolidColorBrush(Colors.Green), "Green");

        Assert.That(((SolidColorBrush)keyFrame.Value!).Color.Value, Is.EqualTo(Colors.Green));
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
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.Value = new DrawablePresenter { Transform = { Value = new TranslateTransform() } };
            holder.TextureValue.Value = texture;
            Element recovered = StoreCorruptAndRestore(
                root,
                holder,
                json => AddUnreadableEasing<TranslateTransform>(
                    DrawableJson(json)[nameof(Drawable.Transform)]!.AsObject(), nameof(TranslateTransform.X))).Element;
            var recoveredHolder = (EditorValueHolder)recovered.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.Value!;
            var presenter = (DrawablePresenter)recoveredTexture.Drawable.Value!;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var editor = new CoreObjectEditorViewModel<Drawable>(new SimplePropertyAdapter<Drawable>(
                (SimpleProperty<Drawable>)recoveredTexture.Drawable, recoveredTexture));
            editor.Accept(Services(recovered, history.History));

            editor.SetTarget(new EllipseShape());

            Assert.Multiple(() =>
            {
                Assert.That(recoveredTexture.Drawable.Value, Is.SameAs(presenter));
                Assert.That(presenter.Target.Expression, Is.Null);
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(history.History.CanUndo, Is.False);
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
            var holder = new EditorValueHolder();
            var texture = new DrawableTextureSource();
            texture.Drawable.Value = new RectShape();
            holder.TextureValue.Value = texture;
            Recovered recovered = StoreCorruptAndRestore(
                root,
                holder,
                json => DrawableJson(json)["$type"] = "[Beutl.Engine]Beutl.Graphics:MissingDrawable");
            Element recoveredElement = recovered.Element;
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.Value!;
            Assert.That(recoveredTexture.Drawable.Value, Is.InstanceOf<FallbackDrawable>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var history = new HistoryScope(recoveredElement);
            var adapter = new SimplePropertyAdapter<TextureSource?>(
                (SimpleProperty<TextureSource?>)recoveredHolder.TextureValue,
                recoveredHolder);
            using var viewModel = new TextureSourceEditorViewModel(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

            viewModel.ChangeToImageTextureSource();
            AssertRepairThenUndoRestoresBytes(recovered, history.History, () =>
            {
                Assert.That(recoveredHolder.TextureValue.Value, Is.SameAs(recoveredTexture));
                Assert.That(recoveredTexture.Drawable.Value, Is.InstanceOf<FallbackDrawable>());
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // Removing the list's only item, or initializing the list, replaces the fallback in it.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ListClear_OfAFallback_IsSavedAndUndoWritesTheFallbackBack(bool initialize)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var group = new FilterEffectGroup();
            group.Children.Add(new Blur());
            Recovered recovered = StoreCorruptAndRestore(
                root,
                new RectShape { FilterEffect = { Value = group } },
                json => FindObject(json, obj => obj[nameof(FilterEffectGroup.Children)] is JsonArray { Count: > 0 })!
                    [nameof(FilterEffectGroup.Children)]!.AsArray()[0]!.AsObject()["$type"]
                    = "[Beutl.Engine]Beutl.Graphics.Effects:MissingFilterEffect");
            Element recoveredElement = recovered.Element;
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            var recoveredGroup = (FilterEffectGroup)recoveredShape.FilterEffect.Value!;
            Assert.That(recoveredGroup.Children.Single(), Is.InstanceOf<FallbackFilterEffect>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var history = new HistoryScope(recoveredElement);
            var adapter = new EnginePropertyAdapter<ICoreList<FilterEffect>>(
                recoveredGroup.Children,
                recoveredGroup);
            using var viewModel = new ListEditorViewModel<FilterEffect>(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

            if (initialize)
                viewModel.Initialize();
            else
                viewModel.RemoveItem(0);

            AssertRepairThenUndoRestoresBytes(recovered, history.History,
                () => Assert.That(recoveredGroup.Children.Single(), Is.InstanceOf<FallbackFilterEffect>()));
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
            Element recovered = StoreCorruptAndRestore(root, CreateTransformHolder(), CorruptTheTransform).Element;
            var recoveredHolder = (NullableRecoveryHolder)recovered.Objects.Single();
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var editor = new CoreObjectEditorViewModel<DisplacementMapTransform>(
                new SimplePropertyAdapter<DisplacementMapTransform>(
                    (SimpleProperty<DisplacementMapTransform>)recoveredHolder.Transform, recoveredHolder));
            editor.Accept(Services(recovered, history.History));

            editor.SetValue(new DisplacementMapTranslateTransform());
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);

            Assert.Multiple(() =>
            {
                Assert.That(recovered.SuppressedStorageSource, Is.Null);
                Assert.That(((NullableRecoveryHolder)CoreSerializer.RestoreFromUri<Element>(recovered.Uri!)
                    .Objects.Single()).Transform.Value, Is.InstanceOf<DisplacementMapTranslateTransform>());
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
            var shape = new RectShape();
            shape.Fill.Value = new DrawableBrush
            {
                Drawable = { Value = new EllipseShape() },
                Transform = { Value = new TranslateTransform() },
            };
            Element recovered = StoreCorruptAndRestore(root, shape, json =>
            {
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
            }).Element;

            var recoveredShape = (RectShape)recovered.Objects.Single();
            var brush = (DrawableBrush)recoveredShape.Fill.Value!;
            Drawable? drawableBefore = brush.Drawable.Value;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var editor = new BrushEditorViewModel(
                new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)recoveredShape.Fill, recoveredShape));
            editor.Accept(Services(recovered, history.History));

            if (!drawableIsUnreadable)
            {
                editor.SetDrawableTarget(null);
                Assert.That(brush.Drawable.Value, Is.SameAs(drawableBefore),
                    "Pointing the kept brush at a target replaces only its drawable.");
            }

            editor.ChangeDrawableType(typeof(RectShape));

            Assert.Multiple(() =>
            {
                if (drawableIsUnreadable)
                {
                    Assert.That(brush.Drawable.Value, Is.InstanceOf<RectShape>());
                    Assert.That(recovered.SuppressedStorageSource, Is.Null);
                }
                else
                {
                    Assert.That(brush.Drawable.Value, Is.SameAs(drawableBefore));
                    Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                    Assert.That(history.History.CanUndo, Is.False);
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
            var shape = new RectShape();
            shape.Transform.Value = new TranslateTransform();
            Element recovered = StoreCorruptAndRestore(
                root,
                shape,
                json => AddUnreadableEasing<TranslateTransform>(
                    json["Objects"]![0]![nameof(RectShape.Transform)]!.AsObject(), nameof(TranslateTransform.X))).Element;
            var recoveredShape = (RectShape)recovered.Objects.Single();
            Transform? unavailable = recoveredShape.Transform.Value;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var editor = new CoreObjectEditorViewModel<Transform>(new SimplePropertyAdapter<Transform>(
                (SimpleProperty<Transform>)recoveredShape.Transform, recoveredShape));
            editor.Accept(Services(recovered, history.History));
            var replacement = new TransformGroup();
            replacement.Children.Add(CreateWithUnreadableEasing<TranslateTransform>(nameof(TranslateTransform.X)));

            editor.SetValue(unavailable, replacement);
            editor.SetValue(replacement);

            Assert.Multiple(() =>
            {
                Assert.That(recoveredShape.Transform.Value, Is.SameAs(unavailable));
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(history.History.CanUndo, Is.False);
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
            Element recovered = StoreCorruptAndRestore(
                root,
                new RectShape { Fill = { Value = new SolidColorBrush(Colors.Red) } },
                json => AddUnreadableEasing<SolidColorBrush>(
                    json[nameof(Element.Objects)]![0]![nameof(Shape.Fill)]!.AsObject(), nameof(Brush.Opacity))).Element;
            var recoveredShape = (RectShape)recovered.Objects.Single();
            Brush? unavailable = recoveredShape.Fill.Value;
            Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
            using var history = new HistoryScope(recovered);
            using var viewModel = new BrushEditorViewModel(new SimplePropertyAdapter<Brush?>(
                (SimpleProperty<Brush?>)recoveredShape.Fill, recoveredShape));
            viewModel.Accept(Services(recovered, history.History));
            JsonObject pastedJson = CoreSerializer.SerializeToJsonObject(new DrawableBrush
            {
                Drawable = { Value = new EllipseShape() },
                Transform = { Value = new TranslateTransform() },
            });
            AddUnreadableEasing<TranslateTransform>(pastedJson[nameof(Brush.Transform)]!.AsObject(), nameof(TranslateTransform.X));

            viewModel.TryPasteJson(pastedJson.ToJsonString());

            Assert.Multiple(() =>
            {
                Assert.That(recoveredShape.Fill.Value, Is.SameAs(unavailable));
                Assert.That(recovered.SuppressedStorageSource, Is.Not.Null);
                Assert.That(history.History.CanUndo, Is.False);
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
            Recovered stored = StoreCorruptAndRestore(root, CreateTransformHolder(), CorruptTheTransform);
            Element recovered = stored.Element;
            var recoveredEffect = (NullableRecoveryHolder)recovered.Objects.Single();
            Assert.That(recoveredEffect.Transform.Value, Is.Null);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(stored.ElementPath), Is.EqualTo(stored.OriginalBytes));
            using var history = new HistoryScope(recovered);
            using var editor = new CoreObjectEditorViewModel<DisplacementMapTransform>(
                new SimplePropertyAdapter<DisplacementMapTransform>(
                    (SimpleProperty<DisplacementMapTransform>)recoveredEffect.Transform, recoveredEffect));
            editor.Accept(Services(recovered, history.History));

            editor.SetNewInstance(typeof(DisplacementMapTranslateTransform));
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(((NullableRecoveryHolder)CoreSerializer.RestoreFromUri<Element>(recovered.Uri!)
                .Objects.Single()).Transform.Value, Is.InstanceOf<DisplacementMapTranslateTransform>());
            Assert.That(history.History.Undo(), Is.True);
            CoreSerializer.StoreToUri(recovered, recovered.Uri!);
            Assert.That(File.ReadAllBytes(stored.ElementPath), Is.EqualTo(stored.OriginalBytes));
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

    [AvaloniaTest]
    [TestCase(PresenterKind.Brush, false)]
    [TestCase(PresenterKind.Transform, false)]
    [TestCase(PresenterKind.FilterEffect, false)]
    [TestCase(PresenterKind.Transform, true)]
    public async Task PresenterClearTarget_OverAFallback_IsSavedAndUndoWritesTheFallbackBack(PresenterKind kind, bool genericEditor)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            var shape = new RectShape();
            SetPresenter(shape, kind);
            Recovered recovered = StoreCorruptAndRestore(
                root,
                shape,
                json => FindObject(json, obj => obj.ContainsKey(nameof(BrushPresenter.Target)))!
                    [nameof(BrushPresenter.Target)]!.AsObject()["$type"] = kind switch
                    {
                        PresenterKind.Brush => "[Beutl.Engine]Beutl.Media:MissingBrush",
                        PresenterKind.Transform => "[Beutl.Engine]Beutl.Graphics.Transformation:MissingTransform",
                        PresenterKind.FilterEffect => "[Beutl.Engine]Beutl.Graphics.Effects:MissingFilterEffect",
                        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                    });
            Element recoveredElement = recovered.Element;
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            Assert.That(GetPresenterTarget(recoveredShape, kind), Is.InstanceOf<IFallback>());
            Assert.That(recoveredElement.SuppressedStorageSource, Is.Null);
            using var history = new HistoryScope(recoveredElement);
            using BaseEditorViewModel viewModel = genericEditor
                ? new CoreObjectEditorViewModel<Transform>(
                    new SimplePropertyAdapter<Transform>((SimpleProperty<Transform>)recoveredShape.Transform, recoveredShape))
                : CreatePresenterEditor(recoveredShape, kind);
            viewModel.Accept(Services(recoveredElement, history.History));

            if (genericEditor)
                ((ICoreObjectEditorViewModel)viewModel).SetTarget(null);
            else
                ClearPresenterTarget(viewModel, kind);

            AssertRepairThenUndoRestoresBytes(recovered, history.History,
                () => Assert.That(GetPresenterTarget(recoveredShape, kind), Is.InstanceOf<IFallback>()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    [TestCase(AnimationDiscardKind.RemoveKeyFrame)]
    [TestCase(AnimationDiscardKind.RemoveAnimation)]
    [TestCase(AnimationDiscardKind.SetExpression)]
    public async Task LastLossyEasingDiscard_ResumesPersistenceInReplacementTransaction(AnimationDiscardKind kind)
    {
        await TestReset.ResetShellAsync();

        string root = CreateRoot();
        try
        {
            Recovered recovered = StoreCorruptAndRestore(root, CreateAnimatedShape(), CorruptTheEasing);
            Element recoveredElement = recovered.Element;
            var shape = (RectShape)recoveredElement.Objects.Single();
            var animation = (KeyFrameAnimation<float>)shape.Width.GetKeyFrameAnimation()!;
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(1));
            using var history = new HistoryScope(recoveredElement);
            var adapter = new AnimatablePropertyAdapter<float>(
                (AnimatableProperty<float>)shape.Width,
                shape);
            using var viewModel = new ValueEditorViewModel<float>(adapter);
            viewModel.Accept(Services(recoveredElement, history.History));

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
            byte[] repairedBytes = File.ReadAllBytes(recovered.ElementPath);
            bool undone = history.History.Undo();
            CoreSerializer.StoreToUri(recoveredElement, recoveredElement.Uri!);
            var restoredAnimation = (KeyFrameAnimation<float>)shape.Width.GetKeyFrameAnimation()!;

            Assert.Multiple(() =>
            {
                Assert.That(repairedBytes, Is.Not.EqualTo(recovered.OriginalBytes));
                Assert.That(undone, Is.True);
                Assert.That(restoredAnimation.KeyFrames, Has.Count.EqualTo(1));
                Assert.That(File.ReadAllBytes(recovered.ElementPath), Is.EqualTo(recovered.OriginalBytes));
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
                shape.Fill.Value = new BrushPresenter
                {
                    Target = { Value = new SolidColorBrush(Colors.Red) },
                };
                break;
            case PresenterKind.Transform:
                shape.Transform.Value = new TransformPresenter
                {
                    Target = { Value = new TranslateTransform() },
                };
                break;
            case PresenterKind.FilterEffect:
                shape.FilterEffect.Value = new FilterEffectPresenter
                {
                    Target = { Value = new Blur() },
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
            PresenterKind.Brush => ((BrushPresenter)shape.Fill.Value!).Target.Value,
            PresenterKind.Transform => ((TransformPresenter)shape.Transform.Value!).Target.Value,
            PresenterKind.FilterEffect => ((FilterEffectPresenter)shape.FilterEffect.Value!).Target.Value,
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

    // Applies the template while the keyframe is being edited, and checks that only the keyframe's value changes.
    private static async Task<KeyFrame<T>> AssertApplyTemplateUpdatesOnlyTheEditingKeyFrame<T>(
        Func<EditorValueHolder, IProperty<T>> select,
        T currentValue,
        T keyFrameValue,
        Func<AnimatablePropertyAdapter<T>, BaseEditorViewModel> createEditor,
        ICoreSerializable template,
        string templateName)
        where T : class?
    {
        await TestReset.ResetShellAsync();

        var holder = new EditorValueHolder();
        var property = (AnimatableProperty<T>)select(holder);
        property.Value = currentValue;
        var keyFrame = new KeyFrame<T> { Value = keyFrameValue };
        property.SetKeyFrameAnimation(CreateAnimation(keyFrame));
        var element = new Element { Uri = new Uri("file:///editor-test.belm") };
        element.AddObject(holder);
        using var history = new HistoryScope(element);
        using BaseEditorViewModel viewModel = createEditor(new AnimatablePropertyAdapter<T>(property, holder));
        viewModel.Accept(Services(element, history.History));
        viewModel.EditingKeyFrame.Value = keyFrame;

        bool applied = viewModel.ApplyTemplate(ObjectTemplateItem.CreateFromInstance(template, templateName));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(property.Value, Is.SameAs(currentValue));
            Assert.That(keyFrame.Value, Is.InstanceOf(template.GetType()));
        });
        return keyFrame;
    }

    // Saves the repaired element, then undoes the repair and saves again. The repair must change the file, and the
    // undo must bring back what could not be loaded (assertRestored checks it) and the file as it was read.
    private static void AssertRepairThenUndoRestoresBytes(Recovered recovered, HistoryManager history, Action assertRestored)
    {
        Element element = recovered.Element;
        CoreSerializer.StoreToUri(element, element.Uri!);
        byte[] repairedBytes = File.ReadAllBytes(recovered.ElementPath);
        bool undone = history.Undo();
        CoreSerializer.StoreToUri(element, element.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(repairedBytes, Is.Not.EqualTo(recovered.OriginalBytes));
            Assert.That(undone, Is.True);
            assertRestored();
            Assert.That(HoldsWhatWasRead(recovered.ElementPath, recovered.OriginalJson), Is.True);
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

    // The element read back from a saved scene, with its file's path, JSON and bytes from before it was read.
    private sealed record Recovered(Element Element, string ElementPath, string OriginalJson, byte[] OriginalBytes);

    // Saves a scene whose one element holds obj, lets corrupt edit the element file and reads the scene back.
    private static Recovered StoreCorruptAndRestore(string root, EngineObject obj, Action<JsonObject> corrupt)
    {
        var sceneUri = new Uri(Path.Combine(root, "scene.scene"));
        string elementPath = Path.Combine(root, "element.belm");
        var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(elementPath) };
        element.AddObject(obj);
        var scene = new Scene(64, 64, "Scene") { Uri = sceneUri };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, sceneUri);

        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        corrupt(elementJson);
        string originalJson = elementJson.ToJsonString();
        File.WriteAllText(elementPath, originalJson);
        byte[] originalBytes = File.ReadAllBytes(elementPath);
        return new Recovered(
            CoreSerializer.RestoreFromUri<Scene>(sceneUri).Children.Single(), elementPath, originalJson, originalBytes);
    }

    private static ServiceVisitor Services(Element element, HistoryManager history)
        => new((typeof(Element), element), (typeof(HistoryManager), history), (typeof(ExtensionProvider), TestShell.Extensions));

    private static RectShape CreateAnimatedShape()
    {
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 32,
        });
        shape.Width.SetKeyFrameAnimation(animation);
        return shape;
    }

    private static NullableRecoveryHolder CreateTransformHolder()
    {
        var holder = new NullableRecoveryHolder();
        holder.Transform.Value = new DisplacementMapTranslateTransform();
        return holder;
    }

    private static void CorruptTheEasing(JsonObject json)
        => FindObject(json, obj => obj.ContainsKey(nameof(KeyFrame.Easing)))![nameof(KeyFrame.Easing)] = MissingEasingType;

    private static void CorruptTheTransform(JsonObject json)
        => json["Objects"]![0]!["Transform"]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingTransform";

    private static JsonObject DrawableJson(JsonObject json)
        => FindObject(json, obj => obj.ContainsKey(nameof(DrawableTextureSource.Drawable)))!
            [nameof(DrawableTextureSource.Drawable)]!.AsObject();

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
        obj.Properties.Single(property => property.Name == propertyName).SetKeyFrameAnimation(animation);
        JsonObject animationJson = CoreSerializer.SerializeToJsonObject(obj)["Animations"]![propertyName]!
            .DeepClone()
            .AsObject();
        animationJson[nameof(IKeyFrameAnimation.KeyFrames)]![0]![nameof(KeyFrame.Easing)] = MissingEasingType;
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

    public enum PresenterKind
    {
        Brush,
        Transform,
        FilterEffect,
    }

    public enum AnimationDiscardKind
    {
        RemoveKeyFrame,
        RemoveAnimation,
        SetExpression,
    }
}
