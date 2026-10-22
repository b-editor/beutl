using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Each factory and command entry point keeps working when an extension throws (#2877).
[TestFixture, NonParallelizable]
public sealed class ExtensionGuardCallSiteTests
{
    [AvaloniaTest]
    public void A_tool_tab_whose_content_or_icon_throws_shows_the_error_text_without_an_icon()
    {
        using var notifications = new NotificationCapture();
        using var dockable = new BeutlToolDockable(new ToolContext(new ThrowingToolTab()), null!);
        var content = new ToolTabContent { DataContext = dockable };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dockable.Icon, Is.Null);
            Assert.That(content.Content, Is.TypeOf<TextBlock>());
            Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.ExtensionFailed, "Throwing tool")));
        }
    }

    [AvaloniaTest]
    public void An_editor_whose_view_throws_shows_the_error_text()
    {
        using var notifications = new NotificationCapture();
        var host = new EditorHostView { DataContext = new EditorContext(new ThrowingEditor()) };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Content, Is.TypeOf<TextBlock>());
            Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.ExtensionFailed, "Throwing editor")));
        }
    }

    [AvaloniaTest]
    public async Task Switching_to_an_editor_whose_context_throws_is_reported_once()
    {
        await TestReset.ResetShellAsync();
        var original = new EditorContext(new ThrowingEditor());
        var tab = new EditorTabItem(original);
        TestShell.Editor.TabItems.Add(tab);
        var extension = new ThrowingEditor();
        ExtensionGuard.SetPackage(extension, "Throwing.Package");
        using var notifications = new NotificationCapture();
        try
        {
            await TestShell.MainViewModel.SwitchEditorAsync(tab, extension);
            await TestShell.MainViewModel.SwitchEditorAsync(tab, extension);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tab.Context.Value, Is.SameAs(original));
                Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.ExtensionFailed, "Throwing editor")));
            }
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public void A_property_editor_extension_that_throws_matches_nothing_and_creates_nothing()
    {
        var target = new TestObject();
        var adapter = new EnginePropertyAdapter<string>(target.NameProperty, target);
        var matching = new ExtensionProvider();
        matching.AddExtensions(new LocalPackage().LocalId, [new ThrowingPropertyEditor(throwOnMatch: true)]);
        var creating = new ExtensionProvider();
        creating.AddExtensions(new LocalPackage().LocalId, [new ThrowingPropertyEditor(throwOnMatch: false)]);
        using var notifications = new NotificationCapture();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new PropertyEditorFactoryAdapter(matching).CreateEditor(adapter), Is.Null);
            Assert.That(new PropertyEditorFactoryAdapter(creating).CreateEditor(adapter), Is.Null);
            Assert.That(notifications.All, Has.Count.EqualTo(2));
        }
    }

    [AvaloniaTest]
    public void A_package_extension_whose_command_throws_is_turned_off()
    {
        var manager = new ContextCommandManager(new ContextCommandSettingsStore([], persist: false));
        var extension = new CommandExtension();
        ExtensionGuard.SetPackage(extension, "Throwing.Package");
        manager.Register(extension);
        var target = new CommandTarget();
        var element = new Border { DataContext = target };
        manager.Attach(element, extension);
        using var notifications = new NotificationCapture();

        element.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = element, Key = Key.V });
        element.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = element, Key = Key.V });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Executions, Is.EqualTo(1));
            Assert.That(notifications.Single().Message, Is.EqualTo(string.Format(ExtensionsStrings.ExtensionTurnedOff, "Throwing.Package")));
        }
    }

    [AvaloniaTest]
    public async Task An_output_extension_whose_context_throws_is_reported_once()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("throwing-output", 320, 180)).Editor;
        using var model = new OutputTabViewModel(editor);
        int profiles = model.Items.Count;
        var extension = new ThrowingOutput();
        ExtensionGuard.SetPackage(extension, "Throwing.Package");
        using var notifications = new NotificationCapture();

        model.AddItem(extension);
        model.AddItem(extension);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Items, Has.Count.EqualTo(profiles));
            Assert.That(notifications.Single().Title, Is.EqualTo(string.Format(ExtensionsStrings.ExtensionFailed, "Throwing output")));
        }
    }

    private sealed class ThrowingOutput : OutputExtension
    {
        public override string DisplayName => "Throwing output";

        public override bool TryCreateControl(
            IEditorContext editorContext, IOutputContext context, IOutputExecutionController execution,
            [NotNullWhen(true)] out Control? control)
            => throw new InvalidOperationException("control");

        public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IOutputContext? context)
            => throw new InvalidOperationException("context");

        public override bool IsSupported(Type type) => true;
    }

    private sealed class ThrowingToolTab : ToolTabExtension
    {
        public override string DisplayName => "Throwing tool";

        public override bool CanMultiple => false;

        public override FAIconSource GetIcon() => throw new InvalidOperationException("icon");

        public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
            => throw new InvalidOperationException("content");

        public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
            => throw new InvalidOperationException("context");
    }

    private sealed class ToolContext(ToolTabExtension extension) : IToolContext
    {
        public ToolTabExtension Extension => extension;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Tool");

        public void Dispose() => IsSelected.Dispose();

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class ThrowingEditor : EditorExtension
    {
        public override string DisplayName => "Throwing editor";

        public override FilePickerFileType GetFilePickerFileType() => new("Test");

        public override FAIconSource? GetIcon() => null;

        public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
            => throw new InvalidOperationException("editor");

        public override bool TryCreateContext(
            CoreObject obj, IEditorContextServices services, [NotNullWhen(true)] out IEditorContext? context)
            => throw new InvalidOperationException("context");

        public override bool MatchFileExtension(string ext) => false;
    }

    private sealed class EditorContext(EditorExtension extension) : IEditorContext
    {
        public CoreObject Object { get; } = new TestObject();

        public EditorExtension Extension => extension;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => null;

        public void Dispose() => IsEnabled.Dispose();
    }

    private sealed class ThrowingPropertyEditor(bool throwOnMatch) : PropertyEditorExtension
    {
        protected internal override void RegisterEditors(PropertyEditorRegistry registry)
        {
            registry.RegisterGroup<IPropertyEditorContext>(
                properties => throwOnMatch ? throw new InvalidOperationException("match") : properties,
                _ => throw new InvalidOperationException("context"),
                _ => throw new InvalidOperationException("control"));
        }
    }

    private sealed class CommandExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new ContextCommandDefinition<CommandTarget>("test.boom", "Boom", null, [new ContextCommandKeyGesture("V")])
            {
                ExecuteOnAsync = (target, _) => target.Execute()
            }
        ];
    }

    private sealed class CommandTarget
    {
        public int Executions { get; private set; }

        public Task Execute()
        {
            Executions++;
            throw new InvalidOperationException("command");
        }
    }

    private sealed class TestObject : CoreObject;
}
