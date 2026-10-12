using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Extensions.FFmpeg.Decoding;

namespace Beutl.UnitTests.Configuration;

// The decoder priorities and editor extension priorities in settings.json name types, and resolve them through
// TypeFormat.ToType, so a type that was renamed or moved still finds its place in them.
[TestFixture]
public class ExtensionConfigFormerNameTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("extension-config-former-name-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    // Beutl 1.x built FFmpeg in as Beutl.Embedding.FFmpeg, so the decoder priority it saved names the FFmpeg decoder
    // that way.
    [Test]
    public void A_decoder_priority_saved_by_1_x_resolves_to_the_FFmpeg_decoder()
    {
        // Loads the FFmpeg extension's assembly, as the app does before it orders the decoders.
        Type decoder = typeof(FFmpegDecoderInfo);
        const string SavedBy1x = "[Beutl.Embedding.FFmpeg].Decoding:FFmpegDecoderInfo";
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, new JsonObject
        {
            ["Extension"] = new JsonObject { ["DecoderPriority"] = new JsonArray(SavedBy1x) },
        }.ToJsonString());
        var config = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

        config.Restore(path);

        ExtensionConfig.TypeLazy priority = config.ExtensionConfig.DecoderPriority.Single();
        Assert.Multiple(() =>
        {
            Assert.That(priority.Type, Is.EqualTo(decoder));
            Assert.That(priority.FormattedTypeName, Is.EqualTo(SavedBy1x), "The setting keeps the name it was saved under.");
        });
    }
}
