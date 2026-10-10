using Beutl.NodeGraph.Generative;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GenerativeInputsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "beutl-generative-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void AClipIsReadWithItsKindAndName()
    {
        string path = Path.Combine(_directory, "clip.WEBM");
        File.WriteAllBytes(path, [1, 2, 3]);

        GenerativeFileInput input = GenerativeInputs.ReadVideoFile(path, "source");

        Assert.Multiple(() =>
        {
            Assert.That(GenerativeInputs.IsSupportedVideoFile(path), Is.True);
            Assert.That(input.Name, Is.EqualTo("source.webm"));
            Assert.That(input.MediaType, Is.EqualTo("video/webm"));
            Assert.That(input.Content, Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void WhatAGenerationCannotTakeIsRefusedBeforeItIsRead()
    {
        string other = Path.Combine(_directory, "clip.mov");
        File.WriteAllBytes(other, [1]);
        string large = Path.Combine(_directory, "large.mp4");
        using (var stream = new FileStream(large, FileMode.Create))
            stream.SetLength(GenerativeInputs.MaxVideoInputBytes + 1);

        Assert.Multiple(() =>
        {
            Assert.That(GenerativeInputs.IsSupportedVideoFile(other), Is.False);
            Assert.That(() => GenerativeInputs.ReadVideoFile(other, "source"), Throws.TypeOf<GenerativeExecutionException>());
            Assert.That(() => GenerativeInputs.ReadVideoFile(large, "source"), Throws.TypeOf<GenerativeExecutionException>());
            Assert.That(
                () => GenerativeInputs.ReadVideoFile(Path.Combine(_directory, "missing.mp4"), "source"),
                Throws.TypeOf<GenerativeExecutionException>());
        });
    }
}
