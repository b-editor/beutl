using Beutl.Collections;
using Beutl.IO;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class FileSerializationTests
{
    [Test]
    public void Serialize()
    {
        var obj = new TestObject();
        obj.Source = new BlobFileSource();

        var directory = Path.GetFullPath(ArtifactProvider.GetArtifactDirectory());
        var blobFilePath = Path.Combine(directory, "blob.txt");
        File.WriteAllText(blobFilePath, "AAAAAAAAAAABBBBBBBBBBBBBBBCCCCCCCCCCCC");
        var blobUri = new Uri(blobFilePath);
        using (var stream = File.OpenRead(blobFilePath))
        {
            obj.Source.ReadFrom(stream, blobUri);
        }

        var objFilePath = Path.Combine(directory, "test_object.json");
        var objUri = new Uri(objFilePath);

        var fs = new VirtualFileSystem();
        var options = new CoreSerializerOptions
        {
            BaseUri = objUri,
            FileSystem = fs,
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.WriteBlobFiles
        };
        var jsonString = CoreSerializer.SerializeToJsonNode(obj, options);

        var deserialized = CoreSerializer.DeserializeFromJsonNode(
            jsonString, typeof(TestObject), options);
    }

    [Test]
    public void SerializeProject()
    {
        var project = new TestProject { Uri = new Uri("file:///project/project.belm") };
        var scene = new TestScene { Uri = new Uri("file:///project/scene1/scene1.scene") };
        var element = new TestElement { Uri = new Uri("file:///project/scene1/element1.belm") };
        scene.Elements.Add(element);
        project.Scenes.Add(scene);

        var fs = new VirtualFileSystem();
        var options = new CoreSerializerOptions
        {
            BaseUri = project.Uri,
            FileSystem = fs,
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.WriteBlobFiles
        };
        var jsonString = CoreSerializer.SerializeToJsonObject(project, options);

        var deserialized = CoreSerializer.DeserializeFromJsonObject(
            jsonString, typeof(TestProject), options);
    }

    public class TestElement : CoreObject
    {
    }

    public class TestScene : CoreObject
    {
        public static readonly CoreProperty<CoreList<TestElement>> ElementsProperty;

        static TestScene()
        {
            ElementsProperty = ConfigureProperty<CoreList<TestElement>, TestScene>(nameof(Elements))
                .Register();
        }

        public TestScene()
        {
            Elements = new CoreList<TestElement>();
        }

        public CoreList<TestElement> Elements
        {
            get => GetValue(ElementsProperty);
            set => SetValue(ElementsProperty, value);
        }
    }

    public class TestProject : CoreObject
    {
        public static readonly CoreProperty<CoreList<TestScene>> ScenesProperty;

        static TestProject()
        {
            ScenesProperty = ConfigureProperty<CoreList<TestScene>, TestProject>(nameof(Scenes))
                .Register();
        }

        public TestProject()
        {
            Scenes = new CoreList<TestScene>();
        }

        public CoreList<TestScene> Scenes
        {
            get => GetValue(ScenesProperty);
            set => SetValue(ScenesProperty, value);
        }
    }

    public class TestObject : CoreObject
    {
        public static readonly CoreProperty<IFileSource?> SourceProperty;

        static TestObject()
        {
            SourceProperty = ConfigureProperty<IFileSource?, TestObject>(nameof(Source))
                .Accessor(o => o.Source, (o, v) => o.Source = v)
                .Register();
        }

        public IFileSource? Source
        {
            get;
            set => SetAndRaise(SourceProperty, ref field, value);
        }
    }
}
