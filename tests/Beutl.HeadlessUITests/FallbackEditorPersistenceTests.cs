using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
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
    [AvaloniaTest]
    public void BrushTryPasteJson_LastFallbackResumesPersistenceInReplacementTransaction()
    {
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
            File.WriteAllBytes(elementPath, originalBytes);
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
