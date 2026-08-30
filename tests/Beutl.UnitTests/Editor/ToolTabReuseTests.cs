using System.Text.Json.Nodes;

using Beutl.Editor.Components.Helpers;
using Beutl.Extensibility;

using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ToolTabReuseTests
{
    [Test]
    public void No_open_tab_leaves_the_caller_to_create_one()
    {
        var context = new TestEditorContext();

        Assert.That(Find(context, target: "a", retargetAnyOpen: true), Is.Null);
    }

    [Test]
    public void The_tab_already_showing_the_target_wins()
    {
        var idle = new FakeTab(null);
        var exact = new FakeTab("a");
        var occupied = new FakeTab("b");
        var context = new TestEditorContext(idle, exact, occupied);

        Assert.That(Find(context, target: "a", retargetAnyOpen: true), Is.SameAs(exact));
    }

    [Test]
    public void An_idle_tab_is_preferred_over_an_occupied_one()
    {
        var occupied = new FakeTab("b");
        var idle = new FakeTab(null);
        var context = new TestEditorContext(occupied, idle);

        Assert.That(Find(context, target: "a", retargetAnyOpen: true), Is.SameAs(idle));
    }

    [Test]
    public void An_occupied_tab_is_retargeted_only_as_a_last_resort()
    {
        var first = new FakeTab("b");
        var second = new FakeTab("c");
        var context = new TestEditorContext(first, second);

        Assert.That(Find(context, target: "a", retargetAnyOpen: true), Is.SameAs(first));
    }

    [Test]
    public void A_menuless_tool_never_retargets_an_occupied_tab()
    {
        var occupied = new FakeTab("b");
        var context = new TestEditorContext(occupied);

        Assert.That(Find(context, target: "a", retargetAnyOpen: false), Is.Null);
    }

    [Test]
    public void A_menuless_tool_still_takes_an_exact_match_or_an_idle_tab()
    {
        var exact = new FakeTab("a");
        var idle = new FakeTab(null);

        Assert.Multiple(() =>
        {
            Assert.That(
                Find(new TestEditorContext(new FakeTab("b"), exact), target: "a", retargetAnyOpen: false),
                Is.SameAs(exact));
            Assert.That(
                Find(new TestEditorContext(new FakeTab("b"), idle), target: "a", retargetAnyOpen: false),
                Is.SameAs(idle));
        });
    }

    private static FakeTab? Find(TestEditorContext context, string target, bool retargetAnyOpen)
    {
        return ToolTabReuse.Find<FakeTab>(
            context,
            t => t.Target == target,
            t => t.Target is null,
            retargetAnyOpen);
    }

    private sealed class FakeTab(string? target) : IToolContext
    {
        public string? Target { get; } = target;

        public ToolTabExtension Extension => null!;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("fake");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class TestEditorContext(params FakeTab[] tabs) : IEditorContext
    {
        public CoreObject Object => null!;

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public object? GetService(Type serviceType) => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return tabs.OfType<T>().FirstOrDefault(condition);
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return FindToolTab<T>(_ => true);
        }

        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => new(true);

        public ValueTask CloseToolTabAsync(IToolContext item)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
