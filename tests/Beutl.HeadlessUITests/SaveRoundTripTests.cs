using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class SaveRoundTripTests
{
    private static void ResetProject() => TestReset.ResetShell();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    [AvaloniaTest]
    public async Task Mutation_made_through_the_editor_survives_save_and_reopen()
    {
        ResetProject();

        Project project = (await ProjectService.Current.CreateProject(
            640, 480, 30, 44100, "saveroundtrip", NewWorkspace("saveroundtrip")))!;
        HeadlessTestHelpers.Settle();
        string projectFile = project.Uri!.LocalPath;
        Scene scene = project.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.FromSeconds(1),
            Length: TimeSpan.FromSeconds(3),
            Layer: 2,
            Name: "RoundTripRect",
            EngineObjectFactory: () => new RectShape { Width = { CurrentValue = 321 } }));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        Guid elementId = element.Id;
        element.ZIndex = 4;
        editor.HistoryManager.Commit("EditZIndex");
        HeadlessTestHelpers.Settle();

        bool saved = await editor.Commands!.OnSave();
        HeadlessTestHelpers.Settle();
        Assert.That(saved, Is.True);

        ResetProject();

        await ProjectService.Current.OpenProject(projectFile);
        HeadlessTestHelpers.Settle();

        Scene reopenedScene = BeutlApplication.Current.Project!.Items.OfType<Scene>().Single();
        Element reopenedElement = reopenedScene.Children.Single(e => e.Id == elementId);
        Assert.That(reopenedElement.Name, Is.EqualTo("RoundTripRect"));
        Assert.That(reopenedElement.ZIndex, Is.EqualTo(4));
        Assert.That(reopenedElement.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));

        RectShape reopenedRect = reopenedElement.Objects.OfType<RectShape>().Single();
        Assert.That(reopenedRect.Width.CurrentValue, Is.EqualTo(321));
    }

    [AvaloniaTest]
    public async Task Saving_writes_the_element_file_to_disk()
    {
        ResetProject();

        Project project = (await ProjectService.Current.CreateProject(
            640, 480, 30, 44100, "savewrites", NewWorkspace("savewrites")))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.Single();
        bool saved = await editor.Commands!.OnSave();
        HeadlessTestHelpers.Settle();

        Assert.That(saved, Is.True);
        Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
        Assert.That(new FileInfo(element.Uri!.LocalPath).Length, Is.GreaterThan(0));
    }
}
