using System.Buffers.Binary;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.UnitTests.Editor;

// Several elements saved as one template keep where they start and lie relative to each other, and the groups among
// them, and each use of the template creates elements of its own.
[TestFixture]
public class ElementSetTemplateTests
{
    private SceneHistoryHarness _harness = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SceneHistoryHarness("beutl_element_set");
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    [Test]
    public void Create_copies_the_elements_with_their_relative_timing_layers_and_groups()
    {
        Scene scene = _harness.Scene;
        Element title = _harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(4), zIndex: 3);
        Element backdrop = _harness.AddElement(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), zIndex: 5);
        Element logo = _harness.AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(1), zIndex: 4);
        Element outside = _harness.AddElement(MediaTime.FromSeconds(9), MediaTime.FromSeconds(1), zIndex: 0);
        scene.Groups.Add([title.Id.Value, backdrop.Id.Value]);
        scene.Groups.Add([logo.Id.Value, outside.Id.Value]);

        ElementSet set = ElementSetFactory.Create(scene, [title, backdrop, logo]);

        Element[] copies = [.. set.Elements];
        Assert.Multiple(() =>
        {
            Assert.That(
                copies.Select(e => (e.Start.Value, e.Length.Value, e.ZIndex.Value)),
                Is.EqualTo(new[]
                {
                    (MediaTime.Zero, MediaTime.FromSeconds(4), 0),
                    (MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 2),
                    (MediaTime.FromSeconds(4), MediaTime.FromSeconds(1), 1),
                }));
            Assert.That(
                copies.Select(e => e.Id.Value).Intersect([title.Id.Value, backdrop.Id.Value, logo.Id.Value]),
                Is.Empty);
            Assert.That(copies.Select(e => e.Uri), Has.All.Null, "A copy must not be saved over its original's file.");
            Assert.That(set.Groups, Has.Count.EqualTo(1), "A group the copies reach only one element of is left out.");
            Assert.That(set.Groups[0], Is.EquivalentTo(new[] { copies[0].Id.Value, copies[1].Id.Value }));
            Assert.That(set.FrameSize.Value, Is.EqualTo(new PixelSize(100, 100)));
            Assert.That(title.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "The originals are left as they were.");
            Assert.That(scene.Children, Does.Contain(title));
        });
    }

    [Test]
    public void A_saved_set_creates_new_elements_and_their_group_each_time_it_is_added()
    {
        Scene scene = _harness.Scene;
        Element first = _harness.AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        Element second = _harness.AddElement(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), zIndex: 2);
        scene.Groups.Add([first.Id.Value, second.Id.Value]);
        ObjectTemplateItem saved = ObjectTemplateItem.CreateFromInstance(
            ElementSetFactory.Create(scene, [first, second]), "Lower third");

        ObjectTemplateItem loaded = ObjectTemplateItem.FromJson(
            ObjectTemplateItem.ToJson(saved),
            "Lower third",
            Path.Combine(_harness.BasePath, "Lower third.json"),
            NullLogger.Instance)!;
        ElementSet[] sets =
        [
            .. Enumerable.Range(0, 2).Select(_ =>
                ((ElementSource.ElementSetTemplate)ElementTemplateResolver.CreateDescription(loaded, MediaTime.Zero, 0)
                    .Source).Factory())
        ];

        Assert.Multiple(() =>
        {
            Assert.That(loaded.BaseType, Is.EqualTo(typeof(ElementSet)));
            Assert.That(ElementTemplateResolver.CanCreateDescription(loaded), Is.True);
            foreach (ElementSet set in sets)
            {
                Assert.That(
                    set.Elements.Select(e => (e.Start.Value, e.ZIndex.Value)),
                    Is.EqualTo(new[] { (MediaTime.Zero, 0), (MediaTime.FromSeconds(1), 1) }));
                Assert.That(set.Groups, Has.Count.EqualTo(1));
                Assert.That(set.Groups[0], Is.EquivalentTo(set.Elements.Select(e => e.Id.Value)));
            }

            Assert.That(
                sets[0].Elements.Select(e => e.Id.Value).Intersect(sets[1].Elements.Select(e => e.Id.Value)),
                Is.Empty,
                "Each use of the template has elements of its own.");
        });
    }

    [Test]
    public async Task The_preview_of_a_set_shows_every_element_at_once()
    {
        // Two squares far apart that are never on screen together: the preview is wide only if it draws both.
        Element left = AddSquare(MediaTime.Zero, zIndex: 0, x: -300);
        Element right = AddSquare(MediaTime.FromSeconds(5), zIndex: 1, x: 300);
        ElementSet set = ElementSetFactory.Create(_harness.Scene, [left, right]);

        byte[]? png = await ObjectTemplatePreviewRenderer.RenderPngAsync(set);

        Assert.That(png, Is.Not.Null);
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        Assert.That(width, Is.GreaterThan(height * 3));
    }

    private Element AddSquare(MediaTime start, int zIndex, float x)
    {
        Element element = _harness.AddElement(start, MediaTime.FromSeconds(1), zIndex);
        element.AddObject(new RectShape
        {
            Width = { Value = 100 },
            Height = { Value = 100 },
            Fill = { Value = new SolidColorBrush(Colors.Red) },
            Transform = { Value = new TranslateTransform(x, 0) },
        });
        return element;
    }
}
