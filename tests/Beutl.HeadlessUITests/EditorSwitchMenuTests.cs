using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
        var notifications = new CaptureNotificationHandler(completed);
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        Action? closeMenu = null;
        TestShell.Editor.TabItems.Add(tab);
        TestShell.Editor.SelectedTabItem.Value = tab;
        TestShell.Extensions.AddExtensions(packageId, [replacement]);
        NotificationService.Handler = notifications;
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
            // Both a replacement and a failure notification complete this signal, so the old
            // behavior fails on the preserved-context assertions rather than a timeout.
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(tab.Context.Value,
                    Is.SameAs(saveSucceeds ? replacement.CreatedContext : original));
                Assert.That(original.DisposeCalls, Is.EqualTo(saveSucceeds ? 1 : 0));
                Assert.That(replacement.CreateCalls, Is.EqualTo(saveSucceeds ? 1 : 0));
                Assert.That(notifications.Items, Has.Count.EqualTo(saveSucceeds ? 0 : 1));
                if (!saveSucceeds && notifications.Items.Count == 1)
                {
                    Assert.That(notifications.Items[0].Type, Is.EqualTo(NotificationType.Error));
                    Assert.That(notifications.Items[0].Title, Is.EqualTo(MessageStrings.UnableToSaveFile));
                    Assert.That(notifications.Items[0].Message, Is.EqualTo("switch.scene"));
                }
            });

            if (!saveSucceeds)
                CaptureFailureNotification(notifications.Items.Single(), nativeMenu);
        }
        finally
        {
            original.SaveCompletion.TrySetResult(false);
            if (original.SaveCalls > 0)
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closeMenu?.Invoke();
            NotificationService.Handler = previousHandler;
            await TestReset.ResetShellAsync();
            TestShell.Extensions.RemoveExtensions(packageId);
        }
    }

    private static void CaptureFailureNotification(Notification notification, bool nativeMenu)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_EDITOR_SWITCH_CAPTURE") is not { Length: > 0 } directory)
            return;

        FAInfoBar infoBar = new NotificationServiceHandler().BuildInfoBar(
            notification, new TaskCompletionSource(), () => { });
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(16), Child = infoBar },
            Width = 400,
            Height = 160,
            RequestedThemeVariant = nativeMenu ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            Assert.That(image, Is.Not.Null);
            image!.Save(Path.Combine(directory, $"save-failed-{nativeMenu}.png"), PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window.Close();
        }
    }

    private static Action CreateMenuClick(
        bool nativeMenu,
        EditorExtension replacement,
        out Action closeMenu)
    {
        // Initialize only the extension menus: showing the shell starts the real app's
        // startup work, which is unrelated to the editor-switch click handlers.
        if (nativeMenu)
        {
            var window = new MacWindow { DataContext = TestShell.MainViewModel };
            closeMenu = () =>
            {
                window.DataContext = null;
                window.Close();
            };
            window.InitExtMenuItems(TestShell.MainViewModel);
            HeadlessTestHelpers.Settle();
            NativeMenu root = NativeMenu.GetMenu(window)!;
            NativeMenu? viewMenu = MacWindow.FindMenuItem(root, Strings.View)!.Menu;
            NativeMenu editors = MacWindow.FindMenuItem(viewMenu, Strings.Editors)!.Menu!;
            NativeMenuItem item = editors.Items.OfType<NativeMenuItem>()
                .Single(item => ReferenceEquals(item.CommandParameter, replacement));
            item.IsEnabled = true;
            return ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked;
        }
        else
        {
            var view = new MainView { DataContext = TestShell.MainViewModel };
            closeMenu = () => view.DataContext = null;
            typeof(MainView).GetMethod("InitExtMenuItems", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, [TestShell.MainViewModel]);
            HeadlessTestHelpers.Settle();
            MenuItem editors = view.FindControl<MenuItem>("editorTabMenuItem")!;
            MenuItem item = editors.Items.OfType<MenuItem>()
                .Single(item => ReferenceEquals(item.DataContext, replacement));
            item.IsVisible = true;
            return () => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
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

    private sealed class CaptureNotificationHandler(TaskCompletionSource completed) : INotificationServiceHandler
    {
        public List<Notification> Items { get; } = [];

        public void Show(Notification notification)
        {
            Items.Add(notification);
            if (notification.Title == MessageStrings.UnableToSaveFile) completed.TrySetResult();
        }
    }
}
