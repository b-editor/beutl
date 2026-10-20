using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EditorSwitchMenuTests
{
    [AvaloniaTest]
    public async Task Switching_scene_editors_flushes_pending_fingerprints_before_context_replacement()
    {
        await TestReset.ResetShellAsync();
        string name = "switch-fingerprints-" + Guid.NewGuid().ToString("N");
        var editor = await TestShell.OpenNewSceneEditorAsync(name);
        var tab = TestShell.Editor.SelectedTabItem.Value!;
        await editor.WaitForMediaFingerprintsAsync();
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, name, "large-image.png");
        var imageSource = new Beutl.Media.Source.ImageSource(); imageSource.ReadFrom(new Uri(path));
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        using (editor.HistoryManager.SuppressRecording())
            editor.Scene.Children.Add(new Element
            {
                Uri = new Uri(Path.Combine(Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, "image.belm")),
                Objects = { new Beutl.Graphics.SourceImage { Source = { CurrentValue = imageSource } } }
            });
        editor.CaptureMediaFingerprints = async (scene, token) =>
        {
            started.TrySetResult(); await release.Task.WaitAsync(token);
            scene.SetMediaFingerprints(new Dictionary<string, MediaFileFingerprint>(scene.MediaFingerprints)
            {
                [new Uri(path).AbsoluteUri] = new MediaFileFingerprint(42, "captured-before-dispose"),
            });
        };
        try
        {
            editor.ScheduleMediaFingerprints(); await started.Task;
            Task switching = TestShell.MainViewModel.SwitchEditorAsync(tab, new ReplacementEditorExtension());
            Assert.That(switching.IsCompleted, Is.False);
            Assert.That(editor.IsDisposingOrDisposed, Is.False);
            release.SetResult(); await switching;
            Assert.That(tab.Context.Value, Is.Not.SameAs(editor));
            var saved = Beutl.Serialization.CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!);
            Assert.That(saved.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256, Is.EqualTo("captured-before-dispose"));
        }
        finally
        {
            release.TrySetResult();
            if (!editor.IsDisposingOrDisposed) await editor.DisposeAsync();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Editor_switch_waits_for_save_and_preserves_context_on_failure(
        bool nativeMenu,
        bool saveSucceeds)
    {
        await TestReset.ResetShellAsync();
        const int packageId = 2464001;
        var document = new Scene { Uri = new Uri("file:///switch.scene") };
        var original = new SavingEditorContext(document);
        var tab = new EditorTabItem(original);
        var replacement = new ReplacementEditorExtension();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? closeMenu = null;
        TestShell.Editor.TabItems.Add(tab);
        TestShell.Editor.SelectedTabItem.Value = tab;
        TestShell.Extensions.AddExtensions(packageId, [replacement]);
        using var notifications = new NotificationCapture(notification =>
        {
            if (notification.Title == MessageStrings.UnableToSaveFile) completed.TrySetResult();
        });
        using IDisposable subscription = tab.Context.Subscribe(context =>
        {
            if (!ReferenceEquals(context, original)) completed.TrySetResult();
        });

        try
        {
            Action click = CreateMenuClick(nativeMenu, replacement, out closeMenu);
            click();

            Assert.Multiple(() =>
            {
                Assert.That(original.SaveCalls, Is.EqualTo(1));
                Assert.That(tab.Context.Value, Is.SameAs(original));
                Assert.That(original.DisposeCalls, Is.Zero);
                Assert.That(replacement.CreateCalls, Is.Zero, "The save must finish before a new context is created.");
            });

            original.SaveCompletion.SetResult(saveSucceeds);
            // Either a replacement or a failure notification completes this signal.
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(tab.Context.Value,
                    Is.SameAs(saveSucceeds ? replacement.CreatedContext : original));
                Assert.That(original.DisposeCalls, Is.EqualTo(saveSucceeds ? 1 : 0));
                Assert.That(replacement.CreateCalls, Is.EqualTo(saveSucceeds ? 1 : 0));
                Assert.That(notifications.All, Has.Count.EqualTo(saveSucceeds ? 0 : 1));
                if (!saveSucceeds && notifications.All.Count == 1)
                {
                    Notification notification = notifications.All.Single();
                    Assert.That(notification.Type, Is.EqualTo(NotificationType.Error));
                    Assert.That(notification.Title, Is.EqualTo(MessageStrings.UnableToSaveFile));
                    Assert.That(notification.Message, Is.EqualTo("switch.scene"));
                }
            });
        }
        finally
        {
            original.SaveCompletion.TrySetResult(false);
            if (original.SaveCalls > 0)
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closeMenu?.Invoke();
            await TestReset.ResetShellAsync();
            TestShell.Extensions.RemoveExtensions(packageId);
        }
    }

    [AvaloniaTest]
    public async Task Switching_editors_holds_the_shared_write_gate_until_the_context_is_replaced()
    {
        await TestReset.ResetShellAsync();
        var document = new Scene { Uri = new Uri("file:///switch.scene") };
        var original = new SavingEditorContext(document);
        var tab = new EditorTabItem(original);
        var replacement = new ReplacementEditorExtension();
        TestShell.Editor.TabItems.Add(tab);
        Task switching = TestShell.MainViewModel.SwitchEditorAsync(tab, replacement);
        Assert.That(original.SaveCalls, Is.EqualTo(1));
        var waiting = TestShell.Editor.BeginProjectFileWriteAsync(CancellationToken.None).AsTask();
        Assert.That(waiting.IsCompleted, Is.False);
        try
        {
            original.SaveCompletion.SetResult(true);
            await switching.WaitAsync(TimeSpan.FromSeconds(5));
            using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(tab.Context.Value, Is.SameAs(replacement.CreatedContext));
            Assert.That(original.DisposeCalls, Is.EqualTo(1));
        }
        finally { original.SaveCompletion.TrySetResult(false); await TestReset.ResetShellAsync(); }
    }

    private static Action CreateMenuClick(
        bool nativeMenu,
        EditorExtension replacement,
        out Action closeMenu)
    {
        // Only the menus are built: showing the shell starts the real app's startup work, which is
        // unrelated to the editor-switch click handlers.
        if (nativeMenu)
        {
            var window = new MacWindow { DataContext = TestShell.MainViewModel };
            closeMenu = () =>
            {
                window.DataContext = null;
                window.Close();
            };
            HeadlessTestHelpers.Settle();
            NativeMenuItem item = NativeItems(NativeMenu.GetMenu(window)!)
                .Single(item => ReferenceEquals(item.CommandParameter, replacement));
            return ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked;
        }
        else
        {
            var view = new MainView { DataContext = TestShell.MainViewModel };
            closeMenu = () => view.DataContext = null;
            HeadlessTestHelpers.Settle();
            MenuItem item = MenuItems(view.FindControl<Menu>("MenuBar")!.Items)
                .Single(item => ReferenceEquals(item.CommandParameter, replacement));
            return () => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }

        static IEnumerable<NativeMenuItem> NativeItems(NativeMenu menu) => menu.Items.OfType<NativeMenuItem>()
            .SelectMany(item => (item.Menu is { } submenu ? NativeItems(submenu) : []).Prepend(item));

        static IEnumerable<MenuItem> MenuItems(ItemCollection items) => items.OfType<MenuItem>()
            .SelectMany(item => MenuItems(item.Items).Prepend(item));
    }

    private class TestEditorContext(CoreObject document, EditorExtension extension) : IEditorContext
    {
        public CoreObject Object => document;
        public EditorExtension Extension => extension;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public int DisposeCalls { get; private set; }
        public object? GetService(Type serviceType) => null;
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public bool OpenToolTab(IToolContext item) => false;
        public void CloseToolTab(IToolContext item) { }

        public void Dispose()
        {
            DisposeCalls++;
            IsEnabled.Dispose();
        }
    }

    private sealed class SavingEditorContext(CoreObject document)
        : TestEditorContext(document, SceneEditorExtension.Instance), ISavableEditorContext
    {
        public TaskCompletionSource<bool> SaveCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCalls { get; private set; }

        public ValueTask<bool> SaveAsync()
        {
            SaveCalls++;
            return new ValueTask<bool>(SaveCompletion.Task);
        }
    }

    private sealed class ReplacementEditorExtension : EditorExtension
    {
        public override string Name => "EditorSwitchReplacement";
        public override string DisplayName => Name;
        public int CreateCalls { get; private set; }
        public IEditorContext? CreatedContext { get; private set; }
        public override FilePickerFileType GetFilePickerFileType() => new(Name);
        public override FAIconSource? GetIcon() => null;
        public override bool MatchFileExtension(string ext) => ext == ".scene";

        public override bool TryCreateEditor(CoreObject obj, [NotNullWhen(true)] out Control? editor)
        {
            editor = new Border();
            return true;
        }

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [NotNullWhen(true)] out IEditorContext? context)
        {
            CreateCalls++;
            context = CreatedContext = new TestEditorContext(obj, this);
            return true;
        }
    }
}
