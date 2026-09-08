using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Editor;

// BEUTL_HOME is redirected to a temp directory by AssemblySetUp, so these write into a throwaway
// templates directory rather than the developer's own.
[TestFixture]
[NonParallelizable]
public class ObjectTemplateServicePreviewTests
{
    [Test]
    public async Task AddFromInstanceAsync_EmbedsThePreviewInTheSavedFile()
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
        };

        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(shape, $"preview-{Guid.NewGuid():N}");

        Assert.That(item, Is.Not.Null);
        Assert.That(item!.Preview, Is.Not.Null.And.Not.Empty);
        Assert.That(item.FilePath, Is.Not.Null);

        JsonNode? saved = JsonNode.Parse(await File.ReadAllTextAsync(item.FilePath!));
        Assert.That(saved!["Preview"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task AddFromInstanceAsync_SavesTheTemplateEvenWithoutAPreview()
    {
        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(new Audio.Effects.AudioEffectGroup(), $"silent-{Guid.NewGuid():N}");

        Assert.That(item, Is.Not.Null);
        Assert.That(item!.Preview, Is.Null);
        Assert.That(File.Exists(item.FilePath), Is.True);
    }
}
