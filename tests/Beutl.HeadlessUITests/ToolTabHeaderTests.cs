using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabHeaderTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Dockable_title_follows_the_context_header()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-follows");

        var context = new FakeToolContext("first");
        var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo("first"));

        context.HeaderSource.Value = "second";
        Assert.That(dockable.Title, Is.EqualTo("second"));

        dockable.Dispose();
        context.HeaderSource.Value = "after-dispose";
        Assert.That(dockable.Title, Is.EqualTo("second"));
    }

    [AvaloniaTest]
    public async Task Blank_header_falls_back_to_the_extension_metadata()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank");

        var context = new FakeToolContext(string.Empty);
        using var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));

        context.HeaderSource.Value = "named";
        Assert.That(dockable.Title, Is.EqualTo("named"));

        context.HeaderSource.Value = string.Empty;
        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));
    }

    private sealed class FakeToolContext(string header) : IToolContext
    {
        public ReactivePropertySlim<string> HeaderSource { get; } = new(header);

        public ToolTabExtension Extension => FakeToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header => HeaderSource;

        public void Dispose()
        {
            IsSelected.Dispose();
            HeaderSource.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class FakeToolExtension : ToolTabExtension
    {
        public static readonly FakeToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "FakeToolTab";

        public override string DisplayName => "Fake tool tab";

        public override string? Header => "Fake tool tab";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext("Fake tool tab");
            return true;
        }
    }
}
