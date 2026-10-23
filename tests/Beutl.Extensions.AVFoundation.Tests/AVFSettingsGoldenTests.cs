using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Serialization;

namespace Beutl.Extensions.AVFoundation.Tests;

// Pins the JSON of the AVFoundation settings before their CoreProperty members become IProperty (#2884 step 9).
[TestFixture]
public class AVFSettingsGoldenTests
{
    [Test]
    public void TheEncoderAndDecoderSettings()
    {
        var json = new JsonObject
        {
            ["Video"] = CoreSerializer.SerializeToJsonObject(new AVFVideoEncoderSettings { JPEGQuality = { Value = 0.5f } }),
            ["Audio"] = CoreSerializer.SerializeToJsonObject(new AVFAudioEncoderSettings
            {
                LinearPcmFloat = { Value = true },
                SampleRate = { Value = 48000 },
            }),
            ["Decoding"] = CoreSerializer.SerializeToJsonObject(new AVFDecodingSettings { MaxVideoBufferSize = { Value = 8 } }),
        };

        AssertGolden(json, "avf-settings");
    }

    // Each section reads back into new settings that write the same JSON, and a section without values keeps the defaults.
    [TestCase("Video", typeof(AVFVideoEncoderSettings))]
    [TestCase("Audio", typeof(AVFAudioEncoderSettings))]
    [TestCase("Decoding", typeof(AVFDecodingSettings))]
    public void EachSectionReadsBack(string section, Type type)
    {
        var expected = (JsonObject)ReadGolden("avf-settings")[section]!;
        var settings = (ICoreSerializable)Activator.CreateInstance(type)!;
        var fromEmpty = (ICoreSerializable)Activator.CreateInstance(type)!;

        CoreSerializer.PopulateFromJsonObject(settings, type, (JsonObject)expected.DeepClone());
        CoreSerializer.PopulateFromJsonObject(fromEmpty, type, new JsonObject());

        Assert.Multiple(() =>
        {
            Assert.That(CoreSerializer.SerializeToJsonObject(settings).ToJsonString(), Is.EqualTo(expected.ToJsonString()));
            Assert.That(WithoutId(CoreSerializer.SerializeToJsonObject(fromEmpty)),
                Is.EqualTo(WithoutId(CoreSerializer.SerializeToJsonObject((ICoreSerializable)Activator.CreateInstance(type)!))));
        });
    }

    private static string WithoutId(JsonObject json)
    {
        json.Remove("Id");
        return json.ToJsonString();
    }

    private static JsonObject ReadGolden(string name, [CallerFilePath] string source = "")
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "Golden", $"{name}.json")))!;

    private static void AssertGolden(JsonObject json, string name, [CallerFilePath] string source = "")
    {
        // Ids are random, so each is numbered in order of first appearance. BEUTL_UPDATE_GOLDEN_JSON=1 rewrites the file.
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string actual = Regex.Replace(
            json.ToJsonString(JsonHelper.SerializerOptions),
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            match =>
            {
                if (!ids.TryGetValue(match.Value, out string? id))
                    ids[match.Value] = id = $"00000000-0000-0000-0000-{ids.Count + 1:D12}";
                return id;
            }) + "\n";
        string path = Path.Combine(Path.GetDirectoryName(source)!, "Golden", $"{name}.json");
        if (Environment.GetEnvironmentVariable("BEUTL_UPDATE_GOLDEN_JSON") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.That(File.Exists(path), Is.True, $"{path} is missing; run with BEUTL_UPDATE_GOLDEN_JSON=1 to write it.");
        Assert.That(actual, Is.EqualTo(File.ReadAllText(path).ReplaceLineEndings("\n")));
    }
}
