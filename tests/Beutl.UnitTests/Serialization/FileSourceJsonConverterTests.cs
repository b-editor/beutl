using System.Text.Json;
using Beutl.IO;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Serialization;

public class FileSourceJsonConverterTests
{
    private static readonly string[][] s_names =
    [
        ["clip one.mp4", "clip%20one.mp4"],
        ["clip#one.mp4", "clip%23one.mp4"],
        ["100%.mp4", "100%25.mp4"],
        ["clip?one.mp4", "clip%3Fone.mp4"],
        ["clip%20one.mp4", "clip%2520one.mp4"],
        ["clip%23one.mp4", "clip%2523one.mp4"],
        ["clip%3Fone.mp4", "clip%253Fone.mp4"],
        ["clip%25one.mp4", "clip%2525one.mp4"]
    ];

    private static readonly JsonSerializerOptions s_options = new()
    {
        Converters = { new TestFileSourceJsonConverter() }
    };

    [TestCaseSource(nameof(s_names))]
    public void Read_Preview8CrossDriveReference_DecodesOnce(string name, string escapedName)
    {
        using var scope = EnterContext();
        string json = JsonSerializer.Serialize($"D:/media/{escapedName}");

        var source = JsonSerializer.Deserialize<IFileSource>(json, s_options)!;

        Assert.Multiple(() =>
        {
            Assert.That(source.Uri.OriginalString, Is.EqualTo($"file:///D:/media/{escapedName}"));
            Assert.That(source.Uri.LocalPath, Is.EqualTo($@"D:\media\{name}"));
            Assert.That(source.Uri.Query, Is.Empty);
            Assert.That(source.Uri.Fragment, Is.Empty);
        });
    }

    [TestCaseSource(nameof(s_names))]
    public void Write_CrossDriveReference_RoundTripsAsExplicitFileUri(string name, string escapedName)
    {
        using var scope = EnterContext();
        string path = $@"D:\media\{name}";
        IFileSource source = new TestFileSource(UriHelper.CreateFromPath(path, windows: true));

        string json = JsonSerializer.Serialize(source, s_options);
        var read = JsonSerializer.Deserialize<IFileSource>(json, s_options)!;

        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Deserialize<string>(json), Is.EqualTo($"file:///D:/media/{escapedName}"));
            Assert.That(read.Uri.LocalPath, Is.EqualTo(path));
            Assert.That(read.Uri.Query, Is.Empty);
            Assert.That(read.Uri.Fragment, Is.Empty);
        });
    }

    [TestCaseSource(nameof(s_names))]
    public void Write_SameDriveReference_RemainsRelativeAndRoundTrips(string name, string escapedName)
    {
        using var scope = EnterContext();
        string path = $@"C:\project\media\{name}";
        IFileSource source = new TestFileSource(UriHelper.CreateFromPath(path, windows: true));

        string json = JsonSerializer.Serialize(source, s_options);
        var read = JsonSerializer.Deserialize<IFileSource>(json, s_options)!;

        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Deserialize<string>(json), Is.EqualTo($"media/{escapedName}"));
            Assert.That(read.Uri.LocalPath, Is.EqualTo(path));
            Assert.That(read.Uri.Query, Is.Empty);
            Assert.That(read.Uri.Fragment, Is.Empty);
        });
    }

    private static IDisposable EnterContext()
        => ThreadLocalSerializationContext.Enter(new JsonSerializationContext(typeof(IFileSource),
            options: new CoreSerializerOptions { BaseUri = new Uri(@"C:\project\main.bep") }));

    private sealed class TestFileSourceJsonConverter : FileSourceJsonConverter
    {
        public override IFileSource CreateInstance(Type typeToConvert) => new TestFileSource(new Uri("file:///"));
    }
}
