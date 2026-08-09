using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
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

        public IProperty<TextureSource?> TextureValue { get; } = Property.Create<TextureSource?>();
    }

    [AvaloniaTest]
    public void BrushTryPasteJson_LastFallbackResumesPersistenceInReplacementTransaction()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredShape = (RectShape)recoveredElement.Objects.Single();
            Assert.That(recoveredShape.Fill.CurrentValue, Is.InstanceOf<IFallback>());

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
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
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
    public void EasingRepair_ResumesPersistenceAndWritesRepairedSidecar()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
    public void CoreObjectApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
    public void GeometryApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
    public void BrushApplyTemplate_UpdatesEditingKeyFrameOnly()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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

    [AvaloniaTest]
    public void TextureDrawableTypeRepair_ResumesPersistenceInReplacementTransaction()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
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
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public void TextureSourceReplacement_ResumesPersistenceInReplacementTransaction()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
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
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [AvaloniaTest]
    public void TextureDrawableTargetRepair_ResumesPersistenceInReplacementTransaction()
    {
        TestReset.ResetShellAsync().GetAwaiter().GetResult();

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
            File.WriteAllText(elementPath, elementJson.ToJsonString());
            byte[] originalBytes = File.ReadAllBytes(elementPath);

            Scene recoveredScene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);
            Element recoveredElement = recoveredScene.Children.Single();
            var recoveredHolder = (EditorValueHolder)recoveredElement.Objects.Single();
            var recoveredTexture = (DrawableTextureSource)recoveredHolder.TextureValue.CurrentValue!;
            Assert.That(recoveredTexture.Drawable.CurrentValue, Is.InstanceOf<FallbackDrawable>());
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
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(originalBytes));
            });
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
}
