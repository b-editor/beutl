using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LutLibraryFlyoutTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("beutl-lut-flyout-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [AvaloniaTest]
    public async Task Choosing_an_entry_writes_the_file_picker_uri_and_undo_restores_the_previous_source()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        string oldPath = Path.Combine(_directory, "old.cube");
        WriteCube(oldPath);
        string chosenPath = Path.Combine(library, "chosen.cube");
        WriteCube(chosenPath, invert: true);
        File.WriteAllText(Path.Combine(library, "broken.cube"), "this is not a lut");

        var effect = new LutEffect();
        var oldSource = new CubeSource();
        oldSource.ReadFrom(UriHelper.CreateFromPath(oldPath));
        effect.Source.Value = oldSource;
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(effect);
        vm.Accept(ServiceVisitor.For(history.History));

        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 260 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Button button = LibraryButton(editor);
            HeadlessTestHelpers.Click(button);
            HeadlessTestHelpers.Render(3);

            Assert.That(button.Flyout, Is.Not.Null);
            Assert.That(button.Flyout!.IsOpen, Is.True);
            LutLibraryViewModel flyout = editor.LibraryViewModel!;
            await HeadlessTestHelpers.WaitUntilAsync(
                () => flyout.Items.Count == 2
                    && flyout.Items.Any(item => item.IsError)
                    && flyout.Items.Any(item => !item.IsError && item.Thumbnail != null),
                message: "The library did not list and thumbnail its files.");

            LutLibraryItemViewModel entry = flyout.Items.Single(item => item.Name == "chosen.cube");
            Button entryButton = editor.LibraryViewControl.GetVisualDescendants().OfType<Button>()
                .Single(candidate => candidate.Classes.Contains("lut-entry")
                    && ReferenceEquals(candidate.Tag, entry));
            HeadlessTestHelpers.Click(entryButton);
            HeadlessTestHelpers.Render(2);

            CubeSource source = effect.Source.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(source, Is.Not.SameAs(oldSource));
                Assert.That(source.Uri, Is.EqualTo(UriHelper.CreateFromPath(chosenPath)));
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
                Assert.That(button.Flyout!.IsOpen, Is.False);
            });

            Assert.That(history.History.Undo(), Is.True);
            Assert.That(effect.Source.Value, Is.SameAs(oldSource));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Library_button_is_disabled_when_the_element_is_locked()
    {
        var effect = new LutEffect();
        var element = new Element { IsLocked = { Value = true } };
        element.AddObject(effect);
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(element);
        vm.Accept(ServiceVisitor.For(history.History, element));

        var editor = (CubeSourceEditor)control;
        var window = new Window { Content = editor, Width = 760, Height = 160 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.That(LibraryButton(editor).IsEffectivelyEnabled, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [Explicit("Writes artifacts/pr-images/lut-library.png for visual review.")]
    public async Task Capture_the_open_library()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "identity.cube"));
        string favorite = Path.Combine(library, "inverted.cube");
        WriteCube(favorite, invert: true);
        File.WriteAllText(Path.Combine(library, "broken.cube"), "this is not a lut");
        File.WriteAllText(
            Path.Combine(library, "library.json"),
            JsonSerializer.Serialize(new
            {
                extraFolders = Array.Empty<string>(),
                favorites = new[] { favorite },
                recent = Array.Empty<string>()
            }));

        var effect = new LutEffect();
        var source = new CubeSource();
        source.ReadFrom(UriHelper.CreateFromPath(Path.Combine(library, "identity.cube")));
        effect.Source.Value = source;
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;

        bool previousOverlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = true;
            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(LibraryButton(editor));
            HeadlessTestHelpers.Render(3);

            LutLibraryViewModel flyout = editor.LibraryViewModel!;
            await HeadlessTestHelpers.WaitUntilAsync(
                () => flyout.Items.Count == 3
                    && flyout.Items.Where(item => !item.IsError).All(item => item.Thumbnail != null),
                message: "The library thumbnails were not ready for the capture.");

            HeadlessTestHelpers.Render(3);
            using WriteableBitmap frame = window.CaptureRenderedFrame()
                ?? throw new AssertionException("No headless frame was rendered.");

            string? root = FindRepositoryRoot();
            Assert.That(root, Is.Not.Null, "The repository root was not found.");
            string output = Path.Combine(root!, "artifacts", "pr-images");
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, "lut-library.png"), PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previousOverlays;
        }
    }

    private static Button LibraryButton(CubeSourceEditor editor)
        => editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "LibraryButton");

    private static void WriteCube(string path, bool invert = false)
    {
        var lines = new List<string> { "LUT_3D_SIZE 2" };
        for (int b = 0; b < 2; b++)
        {
            for (int g = 0; g < 2; g++)
            {
                for (int r = 0; r < 2; r++)
                {
                    lines.Add(invert ? $"{1 - r} {1 - g} {1 - b}" : $"{r} {g} {b}");
                }
            }
        }

        File.WriteAllLines(path, lines);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
