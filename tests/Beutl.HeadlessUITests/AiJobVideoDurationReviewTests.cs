using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiJobVideoDurationReviewTests
{
    [AvaloniaTest]
    [TestCase("edit", null, 72, ".mp4")]
    [TestCase("edit", 8, 72, ".webm")]
    [TestCase("extend", 5, 120, ".mp4")]
    [TestCase("extend", 5, 120, ".webm")]
    [TestCase("motion", 5, 72, ".mp4")]
    [TestCase(null, 4, 72, ".mp4")]
    [TestCase("edit", 8, 0, ".mp4")]
    public async Task SourceJobsImportTheDecodedOutputDuration(string? mode, int? requestedSeconds, int durationTenths, string extension)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "job-video-duration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scene = new Scene(640, 480, "result") { Uri = new Uri(Path.Combine(root, "result.scene")) };
        var decoder = new ResultDecoder(durationTenths);
        var adder = new Mock<IElementAdder>();
        ElementDescription? imported = null;
        adder.Setup(value => value.AddAsync(It.IsAny<IReadOnlyList<ElementDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ElementDescription> descriptions, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                imported = descriptions.Single();
                return ValueTask.FromResult(ElementAddResult.Succeeded([new ElementAddItemResult(imported, new Element(), [])]));
            });
        var editor = new Mock<IAiJobResultEditorContext>();
        editor.SetupGet(value => value.Scene).Returns(scene);
        editor.SetupGet(value => value.ElementAdder).Returns(adder.Object);
        var context = new Mock<IAiJobResultContext>();
        context.SetupGet(value => value.Editor).Returns(editor.Object);
        context.Setup(value => value.CopyContentToAsync(It.IsAny<Uri>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (Uri uri, Stream destination, CancellationToken token) =>
            {
                byte[] bytes = extension == ".mp4"
                    ? [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0]
                    : [0x1a, 0x45, 0xdf, 0xa3];
                await destination.WriteAsync(bytes, token);
                return new AiContentDownload(new("result" + extension, extension == ".mp4" ? "video/mp4" : "video/webm"));
            });
        var job = new AiJob(new("source-result"), AiJobKinds.Video, AiJobStatuses.Succeeded,
            JsonSerializer.SerializeToElement(new { mode, durationSeconds = requestedSeconds }), new("file"),
            new Uri("https://beutl.beditor.net/api/contents/file"), null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        DecoderRegistry.Register(decoder);
        try
        {
            var capability = new VideoAiJobResultCapabilities();
            if (durationTenths == 0)
            {
                try
                {
                    await capability.ApplyAsync(job, context.Object, CancellationToken.None);
                    Assert.Fail("An invalid result duration must be rejected before import.");
                }
                catch (InvalidDataException)
                {
                }
                Assert.That(imported, Is.Null);
            }
            else
            {
                await capability.ApplyAsync(job, context.Object, CancellationToken.None);
                Assert.That(imported, Is.Not.Null);
                Assert.That(imported!.Length, Is.EqualTo(TimeSpan.FromSeconds(mode is null ? requestedSeconds!.Value : durationTenths / 10d)));
                Assert.That(((ElementSource.File)imported.Source).FileName, Does.EndWith(extension));
            }
            string[] probes = decoder.OpenedPaths.Where(path => Path.GetFileName(path).StartsWith("job-video-", StringComparison.Ordinal)).ToArray();
            Assert.That(probes, Has.Length.EqualTo(mode is null ? 0 : 1));
            Assert.That(probes.All(path => Path.GetExtension(path) == extension && !File.Exists(path)), Is.True);
            Assert.That(Directory.EnumerateFiles(AiTemporaryFileStore.GetCategoryDirectory("downloads"), "job-video-*"), Is.Empty);
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ResultDecoder(int durationTenths) : IDecoderInfo
    {
        public string Name => "AI job duration test decoder";
        public List<string> OpenedPaths { get; } = [];
        public IEnumerable<string> VideoExtensions() => [".mp4", ".webm"];
        public IEnumerable<string> AudioExtensions() => [];
        public MediaReader Open(string file, MediaOptions options)
        {
            Assert.That(options.PreferProxy, Is.False);
            OpenedPaths.Add(file);
            return new ResultReader(durationTenths);
        }
    }

    private sealed class ResultReader(int durationTenths) : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", new Rational(durationTenths, 10), new PixelSize(2, 2), new Rational(30, 1));
        public override AudioStreamInfo AudioInfo => throw new InvalidOperationException();
        public override bool HasVideo => true;
        public override bool HasAudio => false;
        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = Ref<Bitmap>.Create(new Bitmap(2, 2));
            return true;
        }
        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }
    }
}
