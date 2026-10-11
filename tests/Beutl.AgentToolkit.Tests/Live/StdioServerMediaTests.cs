using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media.Decoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Live;

// The stdio server loads no extensions, so it registers the decoders the app gets from its built-in
// extensions. DecoderRegistry is process-wide; this project registers no decoders anywhere else.
public sealed class StdioServerMediaTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "stdio-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Headless_server_decodes_a_wav_file_for_rhythm_analysis()
    {
        string wav = Path.Combine(_directory, "beat.wav");
        WriteClickTrack(wav, sampleRate: 44100, durationSeconds: 8, bpm: 120);
        await using ServerConnection server = await ServerConnection.StartAsync(Path.Combine(_directory, "profile"));

        CallToolResult result = await server.Client.CallToolAsync(
            "analyze_audio_rhythm",
            new Dictionary<string, object?> { ["path"] = wav });

        string text = result.Content.OfType<TextContentBlock>().Single().Text;
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("isSuccess").GetBoolean(), Is.True, text);
        JsonElement value = root.GetProperty("value");
        Assert.Multiple(() =>
        {
            Assert.That(value.GetProperty("sampleRate").GetInt32(), Is.EqualTo(44100));
            Assert.That(value.GetProperty("estimatedBpm").GetDouble(), Is.EqualTo(120).Within(2));
        });
    }

    [Test]
    public async Task Headless_server_registers_each_built_in_decoder_once()
    {
        await using (await ServerConnection.StartAsync(Path.Combine(_directory, "first")))
        await using (await ServerConnection.StartAsync(Path.Combine(_directory, "second")))
        {
            IDecoderInfo[] registered = DecoderRegistry.EnumerateDecoder().ToArray();
            Assert.Multiple(() =>
            {
                foreach (IDecoderInfo decoder in BuiltInDecoders.All)
                {
                    Assert.That(registered.Count(item => ReferenceEquals(item, decoder)), Is.EqualTo(1), decoder.Name);
                }

                Assert.That(
                    registered.OfType<AVFDecoderInfo>().Count(),
                    Is.EqualTo(OperatingSystem.IsMacOS() ? 1 : 0));
                Assert.That(DecoderRegistry.GuessDecoder("clip.gif"), Is.Not.Empty);
                Assert.That(DecoderRegistry.GuessDecoder("clip.apng"), Is.Not.Empty);
            });
        }
    }

    // 16-bit mono PCM with a short click on every beat.
    private static void WriteClickTrack(string path, int sampleRate, double durationSeconds, double bpm)
    {
        int sampleCount = (int)Math.Round(durationSeconds * sampleRate);
        var samples = new short[sampleCount];
        int beatSamples = (int)Math.Round(sampleRate * 60d / bpm);
        const int clickLength = 72;
        for (int beatStart = 0; beatStart < sampleCount; beatStart += beatSamples)
        {
            for (int offset = 0; offset < clickLength && beatStart + offset < sampleCount; offset++)
            {
                double envelope = 1 - offset / (double)clickLength;
                samples[beatStart + offset] = (short)(short.MaxValue * 0.9 * envelope * (offset % 2 == 0 ? 1 : -1));
            }
        }

        using var stream = new FileStream(path, FileMode.CreateNew);
        using var writer = new BinaryWriter(stream);
        int dataBytes = sampleCount * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }
    }

    // The server as Program.cs builds it, over in-memory streams.
    private sealed class ServerConnection : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly Stream[] _streams;

        private ServerConnection(IHost host, McpClient client, Stream[] streams)
        {
            _host = host;
            Client = client;
            _streams = streams;
        }

        public McpClient Client { get; }

        public static async Task<ServerConnection> StartAsync(string profileDirectory)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            Stream clientOutput = clientToServer.Writer.AsStream();
            Stream serverInput = clientToServer.Reader.AsStream();
            Stream serverOutput = serverToClient.Writer.AsStream();
            Stream clientInput = serverToClient.Reader.AsStream();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddAgentToolkitServer(profileDirectory);
            builder.Services
                .AddMcpServer()
                .WithStreamServerTransport(serverInput, serverOutput)
                .WithAgentToolkitTools();

            IHost host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientOutput, clientInput))
                .ConfigureAwait(false);
            return new ServerConnection(host, client, [clientOutput, serverInput, serverOutput, clientInput]);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
            foreach (Stream stream in _streams)
            {
                stream.Dispose();
            }
        }
    }
}
