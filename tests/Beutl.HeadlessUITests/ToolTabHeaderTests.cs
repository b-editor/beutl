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

        // HeaderSource deliberately outlives the context (see FakeToolContext.Dispose), so a value
        // pushed here is a live notification and only the dockable's own unsubscribe can stop it.
        dockable.Dispose();
        context.HeaderSource.Value = "after-dispose";
        Assert.Multiple(() =>
        {
            Assert.That(context.HeaderSource.Value, Is.EqualTo("after-dispose"));
            Assert.That(dockable.Title, Is.EqualTo("second"));
        });

        context.HeaderSource.Dispose();
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

    [AvaloniaTest]
    public async Task A_blank_extension_header_falls_back_to_the_display_name()
    {
        // Extension.Name defaults to the bare type name, so it must not be the first fallback.
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank-extension");

        var context = new FakeToolContext(string.Empty, BlankHeaderToolExtension.Instance);
        using var dockable = new BeutlToolDockable(context, editor);

        Assert.Multiple(() =>
        {
            Assert.That(dockable.Title, Is.EqualTo(BlankHeaderToolExtension.Instance.DisplayName));
            Assert.That(dockable.Title, Is.Not.EqualTo(BlankHeaderToolExtension.Instance.Name));
        });
    }

    private sealed class FakeToolContext(string header, ToolTabExtension? extension = null) : IToolContext
    {
        public ReactivePropertySlim<string> HeaderSource { get; } = new(header);

        public ToolTabExtension Extension { get; } = extension ?? FakeToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header => HeaderSource;

        // HeaderSource is left alive so a test can keep publishing after the dockable disposed this
        // context; disposing it here would make "the title stopped following" pass vacuously.
        public void Dispose()
        {
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class BlankHeaderToolExtension : ToolTabExtension
    {
        public static readonly BlankHeaderToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "BlankHeaderToolTab";

        public override string DisplayName => "Blank header tool tab";

        public override string? Header => "   ";

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
            context = new FakeToolContext(string.Empty, Instance);
            return true;
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
