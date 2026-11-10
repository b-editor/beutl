using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Media.Decoding;
using Beutl.Media.Wave;
using Beutl.ProjectSystem;
using NAudio.Wave;

namespace Beutl.AgentToolkit.Tests.Rendering;

// Issue #4204: an AudioSpectrumDrawable inserted without Shape lost the bars its constructor gives it and drew nothing.
[TestFixture]
public sealed class InsertedAudioSpectrumRenderTests
{
    [Test]
    public async Task Spectrum_inserted_without_a_shape_draws_its_bars()
    {
        string directory = TestWorkspace.CreateDirectory();
        string wav = Path.Combine(directory, "noise.wav");
        WriteNoise(wav, sampleRate: 44100, seconds: 1);
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            var scene = new Scene(640, 360, "Scene") { Duration = { Value = MediaTime.FromSeconds(1) } };
            using var session = new AgentToolkitTestSession(scene);
            AgentSessionManager manager = session.CreateManager();

            ToolResult<ApplyEditResponse> apply = new EditTools(manager).ApplyEdit(
                patch: InsertSpectrumOf(wav),
                schemaVersion: SchemaVersion.Current);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

            ToolResult<RenderStillResponse> still = ToolResultReader.Read<RenderStillResponse>(
                await TestRenderTools.Create(manager).RenderStill(Path.Combine(directory, "still.png"), timeSeconds: 0.5));

            Assert.That(still.IsSuccess, Is.True, still.Error?.Message);
            Assert.That(still.Value!.VisibilityAnalysis!.VisiblePixels, Is.GreaterThan(0));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    // Only the type, a name and the sound it shows, as an agent writes it.
    private static JsonObject InsertSpectrumOf(string wav)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                [nameof(Element.Start)] = "00:00:00",
                [nameof(Element.Length)] = "00:00:01",
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(AudioSpectrumDrawable)),
                    [nameof(CoreObject.Name)] = "spectrum",
                    [nameof(AudioSpectrumDrawable.Source)] = new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SourceSound)),
                        [nameof(SourceSound.Source)] = new Uri(wav).AbsoluteUri
                    }
                })
            })
        };
    }

    // Noise has energy at every frequency, so every bar of the spectrum rises.
    private static void WriteNoise(string path, int sampleRate, int seconds)
    {
        var random = new Random(4204);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        for (int frame = 0; frame < sampleRate * seconds; frame++)
        {
            float sample = (float)(random.NextDouble() - 0.5);
            writer.WriteSample(sample);
            writer.WriteSample(sample);
        }
    }
}
