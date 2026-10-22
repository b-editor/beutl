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

    // Beutl 1.x built FFmpeg in as Beutl.Embedding.FFmpeg, and later versions kept the decoder in Beutl.Extensions.FFmpeg
    // until it moved to Beutl.Extensions.FFmpeg.Core, so a saved decoder priority names it either way.
    [TestCase("[Beutl.Embedding.FFmpeg].Decoding:FFmpegDecoderInfo")]
    [TestCase("[Beutl.Extensions.FFmpeg].Decoding:FFmpegDecoderInfo")]
    public void A_decoder_priority_saved_under_a_former_name_resolves_to_the_FFmpeg_decoder(string saved)
    {
        // Loads the FFmpeg decoder's assembly, as the app does before it orders the decoders.
        Type decoder = typeof(FFmpegDecoderInfo);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, new JsonObject
        {
            ["Extension"] = new JsonObject { ["DecoderPriority"] = new JsonArray(saved) },
        }.ToJsonString());
        var config = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;

        config.Restore(path);

        ExtensionConfig.TypeLazy priority = config.ExtensionConfig.DecoderPriority.Single();
        Assert.Multiple(() =>
        {
            Assert.That(priority.Type, Is.EqualTo(decoder));
            Assert.That(priority.FormattedTypeName, Is.EqualTo(saved), "The setting keeps the name it was saved under.");
        });
    }
}
