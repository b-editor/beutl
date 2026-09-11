using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.ViewModels.Dock;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabContentLifetimeTests
{
    [AvaloniaTest]
    public void DisposedTerminal_DoesNotReconfigureAfterContextRebinding()
    {
        using var vm = new TerminalTabViewModel(new TestEditorContext());
        using var view = new TerminalTabView { DataContext = vm };
        view.Dispose();
        var control = view.FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;
        var marker = new Dictionary<string, string> { ["BEUTL_TEST_MARKER"] = "unchanged" };
        control.EnvironmentOverrides = marker;
        view.DataContext = null;
        view.DataContext = vm;
        Assert.That(control.EnvironmentOverrides, Is.SameAs(marker));
    }

    [AvaloniaTest]
    public void Dockable_DisposesReusableContentBeforeItsContext()
    {
        var disposeOrder = new List<string>();
        var context = new DisposableToolContext(disposeOrder);
        var content = new DisposableControl(disposeOrder);
        var dockable = new BeutlToolDockable(context, null!)
        {
            ToolContent = content
        };

        dockable.Dispose();

        Assert.That(disposeOrder, Is.EqualTo(new[] { "content", "context" }));
    }

    [AvaloniaTest]
    public void Dockable_DisposesACombinedContentContextOnlyOnce()
    {
        var combined = new CombinedToolContentContext();
        var dockable = new BeutlToolDockable(combined, null!)
        {
            ToolContent = combined
        };

        dockable.Dispose();
        dockable.Dispose();

        Assert.That(combined.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void Dockable_DisposesContextWhenContentDisposalThrows()
    {
        var disposeOrder = new List<string>();
        var context = new DisposableToolContext(disposeOrder);
        var content = new ThrowingDisposableControl(disposeOrder);
        var dockable = new BeutlToolDockable(context, null!)
        {
            ToolContent = content
        };

        Assert.Throws<InvalidOperationException>(() => dockable.Dispose());
        Assert.That(disposeOrder, Is.EqualTo(new[] { "content", "context" }));
    }

    private sealed class DisposableControl(List<string> disposeOrder) : Control, IDisposable
    {
        public void Dispose()
        {
            disposeOrder.Add("content");
        }
    }

    private sealed class ThrowingDisposableControl(List<string> disposeOrder) : Control, IDisposable
    {
        public void Dispose()
        {
            disposeOrder.Add("content");
            throw new InvalidOperationException("Content disposal failed.");
        }
    }

    private sealed class DisposableToolContext(List<string> disposeOrder) : IToolContext
    {
        public ToolTabExtension Extension => DisposableToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Disposable");

        public void Dispose()
        {
            disposeOrder.Add("context");
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

    private sealed class DisposableToolExtension : ToolTabExtension
    {
        public static readonly DisposableToolExtension Instance = new();

        public override bool CanMultiple => false;

        public override bool ReuseContentAcrossActivation => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = null;
            return false;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = null;
            return false;
        }
    }

    private sealed class CombinedToolContentContext : Control, IToolContext
    {
        public int DisposeCount { get; private set; }

        public ToolTabExtension Extension => DisposableToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Combined");

        public void Dispose()
        {
            DisposeCount++;
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

    private sealed class TestEditorContext : IEditorContext
    {
        public CoreObject Object { get; } = new TestCoreObject();
        public EditorExtension Extension => null!;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public IKnownEditorCommands? Commands => null;
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public bool OpenToolTab(IToolContext item) => false;
        public void CloseToolTab(IToolContext item) { }
        public object? GetService(Type type) => null;
    }
    private sealed class TestCoreObject : CoreObject;
}
