using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Beutl.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class SceneSubtitleExportTests
{
    [AvaloniaTest]
    [TestCase("SRT", "srt", 1855, 2452)]
    [TestCase("ASS", "ass", 1850, 2460)]
    [TestCase("WebVTT", "vtt", 1855, 2452)]
    public async Task Selected_clips_round_trip_through_the_file_menu(
        string formatName, string extension, int laterStart, int laterEnd)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("export-selection");
        Element later = Clip(scene, 1855, 2452, 1, new TextBlock { Text = { Value = "Second — 日本語" } });
        Element earlier = Clip(scene, 210, 890, 0, new TextBlock { Text = { Value = "<b>First</b>\r\n<noparse><note></noparse>" } });
        scene.Children.AddRange([later, Clip(scene, 920, 1320, 0, new TextBlock { Text = { Value = "Unselected" } }), earlier]);
        editor.GetRequiredService<IEditorSelection>().SelectedElements.AddRange([later, earlier]);

        string path = Path.Combine(TestShell.ProjectRoot(editor), $"selected.{extension}");
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        FilePickerSaveOptions? options = null;
        storage.Setup(provider => provider.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .Callback<FilePickerSaveOptions>(value => options = value)
            .ReturnsAsync(PickedFile(path));
        Window window = OpenHost(storage.Object);
        try
        {
            await RunExportAsync(window, dialog =>
            {
                Assert.That(dialog.FindControl<RadioButton>("SelectionSource")!.IsChecked, Is.True);
                Assert.That(dialog.IsPrimaryButtonEnabled, Is.True);
                var picker = dialog.FindControl<ComboBox>("FormatPicker")!;
                Assert.That(picker.SelectedItem!.ToString(), Is.EqualTo("SRT"));
                picker.SelectedItem = picker.Items.Single(item => item!.ToString() == formatName);
                if (extension == "srt")
                    HeadlessCapture.SaveIfRequested(dialog, nameof(SceneSubtitleExportTests), "export-dialog");
            }, captureMenu: extension == "srt");

            Assert.That(options, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(options!.DefaultExtension, Is.EqualTo(extension));
                Assert.That(options.SuggestedFileName, Does.EndWith($".{extension}"));
                Assert.That(options.FileTypeChoices!.Single().Patterns, Is.EqualTo(new[] { $"*.{extension}" }));
            }
            CaptionFormatId format = extension == "vtt" ? CaptionFormats.WebVtt : new CaptionFormatId(extension);
            Assert.That(ReadExport(path, format), Is.EqualTo(new[]
            {
                Cue(210, 890, "First\n<note>"),
                Cue(laterStart, laterEnd, "Second — 日本語"),
            }));
        }
        finally
        {
            await CloseHostAsync(window);
        }
    }

    [AvaloniaTest]
    public async Task Layer_export_reads_nested_template_text_and_reports_non_text_clips()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("export-layers");
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, Name = { Value = "Captions" } });
        var template = new DrawableGroup
        {
            Children =
            {
                new DrawableDecorator { Children = { new TextBlock { Text = { Value = "Template cue" } } } },
                new TextBlock { Text = { Value = "Template cue" } },
            },
        };
        scene.Children.AddRange([
            Clip(scene, 1120, 1750, 2, template),
            Clip(scene, 1880, 2250, 2, new RectShape()),
            Clip(scene, 2300, 2880, 4, new TextBlock { Text = { Value = "Another layer" } }),
            Clip(scene, 0, 1120, 0, new TextBlock { Text = { Value = "Title" } }),
        ]);

        string path = Path.Combine(TestShell.ProjectRoot(editor), "layers.srt");
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        storage.Setup(provider => provider.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ReturnsAsync(PickedFile(path));
        Window window = OpenHost(storage.Object);
        using var notifications = new NotificationCapture(TestShell.Notifications);
        try
        {
            await RunExportAsync(window, dialog =>
            {
                Assert.That(dialog.FindControl<RadioButton>("LayersSource")!.IsChecked, Is.True);
                Assert.That(dialog.FindControl<RadioButton>("SelectionSource")!.IsEnabled, Is.False);
                Assert.That(dialog.IsPrimaryButtonEnabled, Is.False);
                var options = dialog.FindControl<StackPanel>("LayerOptions")!.Children.OfType<CheckBox>().ToArray();
                HeadlessTestHelpers.Click(options.Single(option => Equals(option.Tag, 2)));
                HeadlessTestHelpers.Click(options.Single(option => Equals(option.Tag, 4)));
                Assert.That(dialog.IsPrimaryButtonEnabled, Is.True);
                HeadlessCapture.SaveIfRequested(dialog, nameof(SceneSubtitleExportTests), "export-dialog-layers");
            });

            Assert.That(ReadExport(path, CaptionFormats.Srt), Is.EqualTo(new[]
            {
                Cue(1120, 1750, "Template cue"),
                Cue(2300, 2880, "Another layer"),
            }));
            Notification notification = notifications.Single();
            Assert.That(notification.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notification.Message, Is.EqualTo(string.Format(Strings.ExportSubtitles_SkippedClips, 1)));
        }
        finally
        {
            await CloseHostAsync(window);
        }
    }

    [AvaloniaTest]
    public async Task Both_file_menus_require_an_enabled_scene_editor()
    {
        await TestReset.ResetShellAsync();
        Window window = OpenHost();
        var macWindow = new MacWindow { DataContext = TestShell.MainViewModel };
        try
        {
            MenuItem file = FileMenu(window);
            MenuItem inWindow = ExportItem(file);
            NativeMenuItem native = NativeMenu.GetMenu(macWindow)!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.File).Menu!.Items.OfType<NativeMenuItem>()
                .Single(item => item.Header == Strings.ExportSubtitlesMenu);

            file.Open();
            AssertEnabled(false);
            file.Close();
            (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("export-availability", reset: false);
            file.Open();
            AssertEnabled(true);
            using (TestShell.Editor.SuspendEditor(editor))
            {
                AssertEnabled(false);
            }
            AssertEnabled(true);

            void AssertEnabled(bool enabled)
            {
                HeadlessTestHelpers.Render();
                Assert.Multiple(() =>
                {
                    Assert.That(inWindow.IsEffectivelyEnabled, Is.EqualTo(enabled));
                    Assert.That(native.IsEnabled, Is.EqualTo(enabled));
                });
            }
        }
        finally
        {
            macWindow.DataContext = null;
            macWindow.Close();
            await CloseHostAsync(window);
        }
    }

    private static Element Clip(Scene scene, int start, int end, int layer, EngineObject value)
    {
        var element = new Element
        {
            Start = { Value = MediaTime.FromTimeSpan(TimeSpan.FromMilliseconds(start)) },
            Length = { Value = MediaTime.FromTimeSpan(TimeSpan.FromMilliseconds(end - start)) },
            ZIndex = { Value = layer },
            Objects = { value },
        };
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        element.Uri = new Uri(Path.Combine(directory, $"{element.Id.Value:N}.belm"));
        return element;
    }

    private static CaptionCue Cue(int start, int end, string text)
        => new(TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(end), text);

    private static IReadOnlyList<CaptionCue> ReadExport(string path, CaptionFormatId format)
    {
        CaptionImportResult result = TestShell.MainViewModel.CaptionSerializer.Import(File.ReadAllBytes(path), format);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Diagnostics, Is.Empty);
        return result.Document!.Cues;
    }

    private static IStorageFile PickedFile(string path)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(item => item.Path).Returns(new Uri(path));
        return file.Object;
    }

    private static Window OpenHost(IStorageProvider? storage = null)
    {
        var window = new Window { Width = 1100, Height = 720 };
        if (storage is not null)
            TestStorageProviderFactory.SetProvider(window, storage);
        // Skip MainView's application startup hooks in the test shell.
        window.Show();
        window.Content = new MainView { DataContext = TestShell.MainViewModel };
        HeadlessTestHelpers.Render();
        return window;
    }

    private static async Task CloseHostAsync(Window window)
    {
        ((MainView)window.Content!).DataContext = null;
        window.Close();
        await TestReset.ResetShellAsync();
    }

    private static MenuItem FileMenu(Window window)
    {
        Menu menu = ((MainView)window.Content!).FindControl<Menu>("MenuBar")!;
        menu.IsVisible = true;
        return menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.File));
    }

    private static MenuItem ExportItem(MenuItem fileMenu)
        => fileMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.ExportSubtitlesMenu));

    private static async Task RunExportAsync(Window window, Action<ExportSubtitlesDialog> configure, bool captureMenu = false)
    {
        MenuItem file = FileMenu(window);
        file.Open();
        HeadlessTestHelpers.Render(3);
        MenuItem item = ExportItem(file);
        Assert.That(item.IsEffectivelyEnabled, Is.True);
        if (captureMenu)
            HeadlessCapture.SaveIfRequested(item, nameof(SceneSubtitleExportTests), "file-menu");

        ICommand command = item.Command!;
        HeadlessTestHelpers.Click(item, move: true, ticks: 3);
        ExportSubtitlesDialog? dialog = null;
        try
        {
            dialog = window.GetVisualDescendants().OfType<ExportSubtitlesDialog>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() => !dialog.IsAnimating(Visual.OpacityProperty));
            HeadlessTestHelpers.Render();
            configure(dialog);
            Button save = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PrimaryButton");
            HeadlessTestHelpers.Click(save);
            await HeadlessTestHelpers.WaitUntilAsync(() => command.CanExecute(null));
        }
        finally
        {
            if (dialog?.IsVisible == true)
            {
                dialog.Hide();
                await HeadlessTestHelpers.WaitUntilAsync(() => command.CanExecute(null));
            }
        }
    }
}
