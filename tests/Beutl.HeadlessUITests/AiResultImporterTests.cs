using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiResultImporterTests
{
    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task ImportImage_StagesProjectResource()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-result-importer");
        using var bitmap = new Bitmap(2, 2);
        var importer = new AiResultImporter(
            editor.Scene,
            editor.GetRequiredService<IElementAdder>());

        ElementAddResult result = await importer.ImportImageAsync(
            bitmap,
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                0,
                "AI image"));
        HeadlessTestHelpers.Settle();

        IReadOnlyList<Element> elements = result.Elements;
        string resourcePath = elements.Single().Objects
            .OfType<Beutl.Graphics.SourceImage>()
            .Single()
            .Source.CurrentValue!.Uri.LocalPath;
        Assert.Multiple(() =>
        {
            Assert.That(resourcePath, Does.Contain(Path.Combine("resources", "ai")));
            Assert.That(File.Exists(resourcePath), Is.True);
            Assert.That(elements[0].Name, Is.EqualTo("AI image"));
            Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(resourcePath)!, "*.tmp"), Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task ImportVideoBytes_StagesAndImportsEveryProducedElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-importer");
        var adder = new CapturingElementAdder(producedElementCount: 2);
        var importer = new AiResultImporter(editor.Scene, adder);

        ElementAddResult result = await importer.ImportVideoAsync(
            new byte[] { 1, 2, 3, 4 },
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                0,
                "AI video"));

        Assert.Multiple(() =>
        {
            Assert.That(adder.StagedPath, Does.EndWith(".mp4"));
            Assert.That(File.ReadAllBytes(adder.StagedPath!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(result.Elements, Has.Count.EqualTo(2));
        });
    }

    [AvaloniaTest]
    public async Task RejectedVideoBatch_RemovesStagedProjectResource()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-rejected-importer");
        var adder = new CapturingElementAdder(producedElementCount: 0);
        var importer = new AiResultImporter(editor.Scene, adder);

        ElementAddResult result = await importer.ImportVideoAsync(
            new byte[] { 1, 2, 3, 4 },
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                0,
                "AI video"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure, Is.TypeOf<ElementMaterializationFailure>());
            Assert.That(result.Elements, Is.Empty);
            Assert.That(adder.StagedPath, Is.Not.Null);
            Assert.That(File.Exists(adder.StagedPath), Is.False);
        });
    }

    private sealed class CapturingElementAdder(int producedElementCount) : IElementAdder
    {
        public IElementSourceHandlerRegistry SourceHandlers { get; } = new ElementSourceHandlerRegistry();

        public string? StagedPath { get; private set; }

        public ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ElementDescription description = descriptions.Single();
            StagedPath = ((ElementSource.File)description.Source).FileName;
            var result = new List<Element>(producedElementCount);
            for (int index = 0; index < producedElementCount; index++)
            {
                result.Add(new Element());
            }
            ElementAddResult addResult = result.Count == 0
                ? ElementAddResult.Failed(
                    new ElementMaterializationFailure("The test element could not be materialized."),
                    description)
                : ElementAddResult.Succeeded(
                [
                    new ElementAddItemResult(
                        description,
                        result[0],
                        result.Skip(1).ToArray()),
                ]);
            return ValueTask.FromResult(addResult);
        }
    }
}
