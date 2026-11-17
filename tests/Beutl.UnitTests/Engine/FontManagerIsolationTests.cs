using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine;

public class FontManagerIsolationTests
{
    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    public void FontFreeReconciliationRequiresFontsOnlyWhenTextIsLaidOut(WritingMode mode)
    {
        var block = new TextBlock { Text = { Value = "ABC" }, WritingMode = { Value = mode } };
        var context = CompositionContext.Default;
        using var resource = block.ToResource(context);
        Assert.That(() => resource.Measure(new Size(300, 200)), Throws.InvalidOperationException);

        context.Fonts = CreateManager();
        bool changed = false;
        resource.Reconcile(block, context, ref changed);
        Assert.That(() => resource.Measure(new Size(300, 200)), Throws.Nothing);
        Assert.That(resource.GetTextElements(), Has.Count.GreaterThan(0));

        context.Fonts = null;
        resource.Reconcile(block, context, ref changed);
        Assert.That(() => resource.Measure(new Size(300, 200)), Throws.InvalidOperationException);
        Assert.That(CompositionContext.Default.Fonts, Is.Null);
    }

    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    public void EqualRevisionsFromDifferentManagersInvalidateDetachedLayout(WritingMode mode)
    {
        FontManager first = CreateManager();
        FontManager second = CreateManager();
        var block = new TextBlock
        {
            Text = { Value = "ABC" },
            WritingMode = { Value = mode },
            Size = { Value = 32 },
        };
        var context = new CompositionContext(MediaTime.Zero) { Fonts = first };
        using var resource = block.ToResource(context);
        resource.Measure(new Size(300, 200));
        TextElements elements = resource.GetTextElements();
        FormattedText? vertical = mode == WritingMode.VerticalRl
            ? resource.GetVerticalLayout().Columns[0].Runs[0].Text : null;
        int version = resource.Version;

        Assert.That(second.Revision, Is.EqualTo(first.Revision));
        context.Fonts = second;
        bool changed = false;
        resource.Reconcile(block, context, ref changed);
        resource.Measure(new Size(300, 200));

        Assert.Multiple(() =>
        {
            Assert.That(resource.Version, Is.GreaterThan(version));
            Assert.That(elements.IsDisposed, Is.True);
            Assert.That(vertical?.IsDisposed ?? true, Is.True);
        });
        foreach (Span<FormattedText> line in resource.GetTextElements().Lines)
            foreach (FormattedText text in line)
                Assert.That(text.Fonts, Is.SameAs(second));
        if (mode == WritingMode.VerticalRl)
            Assert.That(resource.GetVerticalLayout().Columns[0].Runs[0].Text.Fonts, Is.SameAs(second));
    }

    [Test]
    public void RenderNodeKeepsARevisionSnapshotAndTextEqualityIncludesTheOwner()
    {
        FontManager first = CreateManager();
        FontManager second = CreateManager();
        using var text = new FormattedText(first) { Text = "ABC", Size = 32 };
        using var other = new FormattedText(second) { Text = "ABC", Size = 32 };
        using var node = new TextRenderNode(text, null, null);
        var scaled = text.GetTextBlob(2);
        Assert.That(text.Equals(other), Is.False);
        Assert.That(node.Update(text, null, null), Is.False);
        first.SetFallbackFonts([FontFamily.Default], null);
        Assert.That(node.Update(text, null, null), Is.True);
        Assert.That(text.GetTextBlob(2), Is.Not.SameAs(scaled));
        Assert.That(node.Update(text, null, null), Is.False);
        Assert.That(node.Update(other, null, null), Is.True);
    }

    [Test]
    public void ProjectFontsAndFallbackOrderBelongToTheirManager()
    {
        FontManager first = CreateManager();
        FontManager second = CreateManager();
        var family = new FontFamily("Beutl Test Variable");
        string root = Directory.CreateTempSubdirectory("beutl-font-isolation-").FullName;
        try
        {
            string directory = Directory.CreateDirectory(Path.Combine(root, "resources", "fonts")).FullName;
            using (Stream input = typeof(FontManagerIsolationTests).Assembly
                       .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.BeutlTestVariable.ttf")!)
            using (Stream output = File.Create(Path.Combine(directory, "font.ttf")))
                input.CopyTo(output);
            var project = new Project { Uri = new Uri(Path.Combine(root, "project.bep")) };
            var block = new TextBlock { Text = { Value = "IIII" }, FontFamily = { Value = family } };
            var context = new CompositionContext(MediaTime.Zero) { Fonts = first };
            using var resource = block.ToResource(context);
            resource.Measure(new Size(300, 200));
            int beforeLoad = resource.Version;
            long secondRevision = second.Revision;

            first.LoadProjectFonts(project);
            first.SetFallbackFonts([family], null);
            bool changed = false;
            resource.Reconcile(block, context, ref changed);
            resource.Measure(new Size(300, 200));
            var face = first.ResolveSkia(new Typeface(family));
            Assert.Multiple(() =>
            {
                Assert.That(first.IsRegistered(family), Is.True);
                Assert.That(second.IsRegistered(family), Is.False);
                Assert.That(second.Revision, Is.EqualTo(secondRevision));
                Assert.That(second.GetFallbackFamilies().Families, Is.Empty);
                Assert.That(resource.Version, Is.GreaterThan(beforeLoad));
            });

            int beforeClear = resource.Version;
            first.ClearProjectFonts(project);
            resource.Reconcile(block, context, ref changed);
            resource.Measure(new Size(300, 200));
            Assert.Multiple(() =>
            {
                Assert.That(first.IsRegistered(family), Is.False);
                Assert.That(resource.Version, Is.GreaterThan(beforeClear));
                Assert.That(face.Handle, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(face.FamilyName, Is.EqualTo(family.Name));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void DefaultAndFontSerializationDoNotRequireACatalog()
    {
        Assert.That(CompositionContext.Default.Fonts, Is.Null);
        var block = new TextBlock();
        JsonObject json = CoreSerializer.SerializeToJsonObject(block);
        Assert.That(json["FontFamily"]!.GetValue<string>(), Is.EqualTo(FontFamily.Default.Name));
        foreach (FontFamily? family in new FontFamily?[] { new("Missing Named Font"), null })
        {
            block.FontFamily.Value = family;
            json = CoreSerializer.SerializeToJsonObject(block);
            var restored = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
            Assert.That(restored.FontFamily.Value, Is.EqualTo(family));
        }
        json.Remove("FontFamily");
        var legacy = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
        Assert.That(legacy.FontFamily.Value, Is.SameAs(FontFamily.Default));
        var fonts = CreateManager();
        using var resource = block.ToResource(new CompositionContext(MediaTime.Zero) { Fonts = fonts });
        resource.Measure(new Size(300, 200));
        Assert.That(block.FontFamily.Value, Is.Null);
    }

    private static FontManager CreateManager()
    {
        var configuration = new FontConfig();
        configuration.FontDirectories.Clear();
        return new FontManager(configuration);
    }
}
