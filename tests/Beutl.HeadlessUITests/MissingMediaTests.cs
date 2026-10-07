using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

public class MissingMediaTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    private static string NewDirectory() => Directory.CreateDirectory(
        Path.Combine(BeutlHomeIsolation.CurrentHome!, "relink-" + Guid.NewGuid().ToString("N"))).FullName;

    private static Element AddImage(Scene scene, string path, string name = "Clip")
    {
        var element = new Element { Name = name, Length = TimeSpan.FromSeconds(3) };
        if (scene.Uri != null) element.Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri.LocalPath)!, $"{element.Id}.belm"));
        element.Objects.Add(new SourceImage { Source = { CurrentValue = ImageSource.Open(path) } });
        scene.Children.Add(element);
        return element;
    }

    [AvaloniaTest]
    public void Detection_groups_references_and_finds_animated_and_font_sources()
    {
        string directory = NewDirectory();
        var scene = new Scene();
        string imagePath = Path.Combine(directory, "missing.png");
        Element first = AddImage(scene, imagePath, "First");
        Element second = AddImage(scene, imagePath, "Second");
        var video = new SourceVideo();
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = TimeSpan.Zero, Value = VideoSource.Open(Path.Combine(directory, "missing.mp4")) });
        video.Source.Animation = animation;
        first.Objects.Add(video);
        first.Objects.Add(new SourceSound { Source = { CurrentValue = SoundSource.Open(Path.Combine(directory, "missing.wav")) } });
        ((SourceImage)first.Objects[0]).FilterEffect.CurrentValue = new Beutl.Graphics.Effects.LutEffect
        { Source = { CurrentValue = CubeSource.Open(Path.Combine(directory, "missing.cube")) } };
        first.Objects.Add(new Beutl.Graphics.Shapes.TextBlock { FontFamily = { CurrentValue = new Beutl.Media.FontFamily("Absent Test Font") } });

        var missing = new MissingMediaService(_ => false).FindMissing(scene);
        Assert.Multiple(() =>
        {
            Assert.That(missing.Select(item => item.Kind), Is.EquivalentTo(new[]
                { MissingMediaKind.Image, MissingMediaKind.Video, MissingMediaKind.Sound, MissingMediaKind.Cube, MissingMediaKind.Font }));
            Assert.That(missing.Single(item => item.Kind == MissingMediaKind.Image).References.Select(reference => reference.Element),
                Is.EquivalentTo(new[] { first, second }));
            Assert.That(missing.Single(item => item.Kind == MissingMediaKind.Video).References.Single().Element, Is.SameAs(first));
        });
    }

    [AvaloniaTest]
    [TestCase("asset.png")]
    [TestCase("asset one.png")]
    [TestCase("素材 一.png")]
    public async Task Ambiguous_names_are_skipped_and_saved_hashes_find_renamed_files(string fileName)
    {
        string directory = NewDirectory();
        string original = Path.Combine(directory, fileName);
        File.WriteAllText(original, "original");
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "scene.scene")) };
        AddImage(scene, original);
        var service = new MissingMediaService(_ => true);
        await service.UpdateFingerprintsAsync(scene);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        string search = Path.Combine(directory, "moved");
        Directory.CreateDirectory(Path.Combine(search, "a"));
        Directory.CreateDirectory(Path.Combine(search, "b"));
        File.WriteAllText(Path.Combine(search, "a", fileName), "wrongone");
        File.WriteAllText(Path.Combine(search, "b", fileName), "alsowrng");
        string renamed = Path.Combine(search, "renamed.png");
        File.Move(original, renamed);
        var restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var item = service.FindMissing(restored).Single();
        Assert.That(item.Fingerprint, Is.Not.Null);
        Assert.That((await service.FindMatchesAsync([item], search))[item], Is.EqualTo(renamed));
        restored.MediaFingerprints.Clear();
        Assert.That(await service.FindMatchesAsync(service.FindMissing(restored), search), Is.Empty);
    }

    [AvaloniaTest]
    public async Task Invalid_candidate_does_not_change_the_original_reference()
    {
        string directory = NewDirectory();
        var scene = new Scene();
        string original = Path.Combine(directory, "missing.png");
        AddImage(scene, original);
        string invalid = Path.Combine(directory, "invalid.png");
        File.WriteAllText(invalid, "not an image");
        var service = new MissingMediaService(_ => true);
        MissingMedia item = service.FindMissing(scene).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ValidateAsync(item, invalid));
        Assert.That(item.References.Single().Source!.Uri.LocalPath, Is.EqualTo(original));
    }

    [AvaloniaTest]
    public async Task Malformed_luts_are_rejected_and_relinked_luts_discard_the_old_cache()
    {
        string directory = NewDirectory();
        string first = Path.Combine(directory, "first.cube");
        string second = Path.Combine(directory, "second.cube");
        File.WriteAllText(first, "LUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
        File.WriteAllText(second, "LUT_1D_SIZE 2\n1 0 0\n0 1 1\n");
        var source = CubeSource.Open(first);
        using var resource = source.ToResource(CompositionContext.Default);
        CubeFile original = resource.Cube!;
        Assert.That(original, Is.Not.Null);
        ResourceRelocationService.RelinkFileSource(source, new Uri(second));
        bool updateOnly = false;
        resource.Update(source, CompositionContext.Default, ref updateOnly);
        Assert.That(resource.Cube, Is.Not.SameAs(original));
        Assert.That(resource.Cube!.Data[0].X, Is.EqualTo(1));
        string invalid = Path.Combine(directory, "truncated.cube");
        File.WriteAllText(invalid, "LUT_1D_SIZE 2\n0 0 0\n");
        var missing = new MissingMedia(MissingMediaKind.Cube, new Uri(Path.Combine(directory, "missing.cube")), null, [], null);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new MissingMediaService().ValidateAsync(missing, invalid).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [AvaloniaTest]
    public void Relinking_updates_an_already_open_property_editor_without_replacing_the_source()
    {
        string directory = NewDirectory();
        string oldPath = Path.Combine(directory, "missing.png");
        string newPath = Path.Combine(directory, "replacement.png");
        File.WriteAllBytes(newPath, s_png);
        var image = new SourceImage { Source = { CurrentValue = ImageSource.Open(oldPath) } };
        using var vm = new Beutl.ViewModels.Editors.ImageSourceEditorViewModel(
            new Beutl.PropertyAdapters.EnginePropertyAdapter<ImageSource?>(image.Source, image));
        ImageSource original = image.Source.CurrentValue!;
        ResourceRelocationService.RelinkFileSource(original, new Uri(newPath));
        Assert.That(vm.FileInfo.Value!.FullName, Is.EqualTo(newPath));
        Assert.That(image.Source.CurrentValue, Is.SameAs(original));
    }

    [AvaloniaTest]
    public async Task Font_matches_are_bundled_and_available_after_reopening_the_project()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditorAsync();
        string directory = NewDirectory();
        string fontPath = Path.Combine(directory, "renamed-font.ttf");
        using (var input = typeof(MissingMediaTests).Assembly.GetManifestResourceStream("ProjectFontFixture.ttf")!)
        using (var output = File.Create(fontPath)) input.CopyTo(output);
        var family = new Beutl.Media.FontFamily("Beutl Test Variable");
        var text = new Beutl.Graphics.Shapes.TextBlock { Text = { CurrentValue = "Relink test" }, FontFamily = { CurrentValue = family } };
        using (editor.HistoryManager.SuppressRecording())
        {
            Element element = AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
            element.Objects.Clear();
            element.Objects.Add(text);
        }
        using var textResource = text.ToResource(CompositionContext.Default);
        var fallbackText = textResource.GetTextElements();
        using var vm = new MissingMediaDialogViewModel(editor);
        Assert.That(vm.Rows.Single().Media.Kind, Is.EqualTo(MissingMediaKind.Font));
        await vm.FindInDirectoryAsync(directory);
        Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
        bool updateOnly = false;
        textResource.Update(text, CompositionContext.Default, ref updateOnly);
        Assert.That(textResource.GetTextElements(), Is.Not.SameAs(fallbackText), "Relinking refreshes a cached fallback font without changing the text.");
        string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "resources", "fonts", "renamed-font.ttf")), Is.True);
        await TestReset.ResetShellAsync();
        await TestShell.Project.OpenProject(projectPath);
        Assert.That(FontManager.Instance.IsRegistered(family), Is.True);
        Assert.That(new MissingMediaService().FindMissing(BeutlApplication.Current.Project!.Items.OfType<Scene>().Single()), Is.Empty);
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task One_folder_repairs_300_assets_and_persists_their_references()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditorAsync();
        string directory = NewDirectory();
        string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
        Guid sceneId = editor.Scene.Id;
        using (editor.HistoryManager.SuppressRecording())
        {
            for (int i = 0; i < 300; i++)
            {
                string name = $"image-{i:D3}.png";
                AddImage(editor.Scene, Path.Combine(directory, "old", name), name);
                File.WriteAllBytes(Path.Combine(directory, name), s_png);
            }
        }
        using var vm = new MissingMediaDialogViewModel(editor);
        Assert.That(vm.Rows, Has.Count.EqualTo(300));
        await vm.FindInDirectoryAsync(directory);
        Assert.That(vm.Rows.All(row => row.ReplacementPath.Value != null), Is.True, vm.Error.Value);
        Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
        Assert.That(editor.HistoryManager.UndoCount, Is.Zero, "Repairs are separate from editing history.");
        Assert.That(GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory, Is.EqualTo(directory));
        await TestReset.ResetShellAsync();
        await TestShell.Project.OpenProject(projectPath);
        Scene reopened = BeutlApplication.Current.Project!.Items.OfType<Scene>().Single(scene => scene.Id == sceneId);
        Assert.That(new MissingMediaService().FindMissing(reopened), Is.Empty);
        Assert.That(reopened.Children, Has.Count.EqualTo(300));
        Assert.That(reopened.MediaFingerprints, Has.Count.EqualTo(300));
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public void Missing_image_and_video_resources_have_a_visible_offline_frame()
    {
        string directory = NewDirectory();
        var image = ImageSource.Open(Path.Combine(directory, "absent.png"));
        using var imageResource = image.ToResource(CompositionContext.Default);
        var video = VideoSource.Open(Path.Combine(directory, "absent.mp4"));
        using var videoResource = video.ToResource(CompositionContext.Default);
        Assert.Multiple(() =>
        {
            Assert.That(imageResource.IsOffline, Is.True);
            Assert.That(imageResource.Bitmap, Is.Not.Null);
            Assert.That(imageResource.FrameSize, Is.EqualTo(new Beutl.Media.PixelSize(320, 180)));
            Assert.That(videoResource.IsOffline, Is.True);
            Assert.That(videoResource.Read(0, out var frame), Is.True);
            frame?.Dispose();
        });
        string capture = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "offline-placeholder.png");
        Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
        imageResource.Bitmap!.Save(capture, EncodedImageFormat.Png);
        File.WriteAllBytes(Path.Combine(directory, "restored.png"), s_png);
        ResourceRelocationService.RelinkFileSource(image, new Uri(Path.Combine(directory, "restored.png")));
        bool updateOnly = false;
        imageResource.Update(image, CompositionContext.Default, ref updateOnly);
        Assert.That(imageResource.IsOffline, Is.False);
        Assert.That(imageResource.FrameSize.Width, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_missing_model_survives_deserialization_and_relink_preserves_mesh_edits()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "triangle.obj");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var source = new ModelSource();
        source.ReadFrom(new Uri(path));
        var model = new Model3D { Source = { CurrentValue = source } };
        model.Children.Single().Name = "Edited mesh";
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "model.scene")) };
        var element = AddImage(scene, Path.Combine(directory, "offline.png"));
        element.Objects.Clear();
        element.Objects.Add(model);
        CoreSerializer.StoreToUri(scene, scene.Uri);
        string moved = Path.Combine(directory, "moved.obj");
        File.Move(path, moved);
        var restored = CoreSerializer.RestoreFromUri<Scene>(scene.Uri);
        var restoredModel = (Model3D)restored.Children.Single().Objects.Single();
        var child = restoredModel.Children.Single();
        var missing = new MissingMediaService(_ => true).FindMissing(restored).Single();
        Assert.That(missing.Kind, Is.EqualTo(MissingMediaKind.Model));
        ResourceRelocationService.RelinkFileSource(restoredModel.Source.CurrentValue!, new Uri(moved));
        Assert.That(restoredModel.Children.Single(), Is.SameAs(child));
        Assert.That(child.Name, Is.EqualTo("Edited mesh"));
    }

    [AvaloniaTest]
    public async Task Same_uri_image_relink_recovers_the_existing_preview_resource_through_the_dialog()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditorAsync();
        string path = Path.Combine(NewDirectory(), "restored.png");
        Element element;
        using (editor.HistoryManager.SuppressRecording()) element = AddImage(editor.Scene, path);
        var drawable = (SourceImage)element.Objects.Single();
        ImageSource source = drawable.Source.CurrentValue!;
        var compositor = editor.Renderer.Value.Compositor;
        var initial = (SourceImage.Resource)compositor.EvaluateGraphics(TimeSpan.Zero).Objects.Single();
        Assert.That(initial.Source!.IsOffline, Is.True);
        using var vm = new MissingMediaDialogViewModel(editor);
        File.WriteAllBytes(path, s_png);
        // A second consumer can populate the shared cache before the relink is applied.
        using var shared = source.ToResource(CompositionContext.Default);
        Assert.That(shared.IsOffline, Is.False);
        await vm.SetReplacementAsync(vm.Rows.Single(), path);
        Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
        var repaired = (SourceImage.Resource)compositor.EvaluateGraphics(TimeSpan.Zero).Objects.Single();
        Assert.That(repaired, Is.SameAs(initial));
        Assert.That(repaired.Source!.IsOffline, Is.False);
        Assert.That(repaired.Source.Bitmap, Is.Not.SameAs(shared.Bitmap), "Relinking must discard a live shared decode at the same URI.");
        Assert.That(repaired.Source.FrameSize.Width, Is.EqualTo(1));
        Assert.That(drawable.Source.CurrentValue, Is.SameAs(source));
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public void Same_uri_lut_relink_recovers_existing_resources_and_discards_shared_data()
    {
        string path = Path.Combine(NewDirectory(), "restored.cube");
        var source = CubeSource.Open(path);
        using var existing = source.ToResource(CompositionContext.Default);
        Assert.That(existing.Cube, Is.Null);
        File.WriteAllText(path, "LUT_1D_SIZE 2\n0 0 0\n1 1 1\n");
        using var shared = source.ToResource(CompositionContext.Default);
        Assert.That(shared.Cube, Is.Not.Null);
        ResourceRelocationService.RelinkFileSource(source, source.Uri);
        bool updateOnly = false;
        existing.Update(source, CompositionContext.Default, ref updateOnly);
        Assert.That(existing.Cube, Is.Not.Null);
        Assert.That(existing.Cube, Is.Not.SameAs(shared.Cube));
        CubeFile recovered = existing.Cube!;
        existing.Update(source, CompositionContext.Default, ref updateOnly);
        Assert.That(existing.Cube, Is.SameAs(recovered), "An unchanged source should not reopen on every frame.");
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Same_uri_video_and_audio_relink_reopens_existing_readers_once(bool video)
    {
        string path = Path.Combine(NewDirectory(), video ? "restored.relink-video" : "restored.relink-audio");
        var decoder = new RestoredMediaDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            MediaSource source = video ? VideoSource.Open(path) : SoundSource.Open(path);
            using var existing = source.ToResource(CompositionContext.Default);
            using var second = source.ToResource(CompositionContext.Default);
            static MediaReader? Reader(EngineObject.Resource resource) => resource switch
            {
                VideoSource.Resource v => v.MediaReader,
                SoundSource.Resource s => s.MediaReader,
                _ => null
            };
            Assert.That(Reader(existing), Is.Null);
            File.WriteAllBytes(path, [1]);
            ResourceRelocationService.RelinkFileSource(source, source.Uri);
            bool updateOnly = false;
            existing.Update(source, CompositionContext.Default, ref updateOnly);
            second.Update(source, CompositionContext.Default, ref updateOnly);
            Assert.That(Reader(existing), Is.Not.Null);
            Assert.That(Reader(second), Is.SameAs(Reader(existing)));
            Assert.That(decoder.OpenCount, Is.EqualTo(1));
            using var shared = source.ToResource(CompositionContext.Default);
            MediaReader previous = Reader(shared)!;
            ResourceRelocationService.RelinkFileSource(source, source.Uri);
            existing.Update(source, CompositionContext.Default, ref updateOnly);
            second.Update(source, CompositionContext.Default, ref updateOnly);
            Assert.That(Reader(existing), Is.Not.SameAs(previous));
            Assert.That(decoder.OpenCount, Is.EqualTo(2));
            existing.Update(source, CompositionContext.Default, ref updateOnly);
            second.Update(source, CompositionContext.Default, ref updateOnly);
            Assert.That(decoder.OpenCount, Is.EqualTo(2), "The relink should cause a single shared reopen, not repeated opens per frame.");
        }
        finally { DecoderRegistry.Unregister(decoder); }
    }

    private sealed class RestoredMediaDecoder : IDecoderInfo
    {
        public string Name => "Restored media test decoder";
        public int OpenCount { get; private set; }
        public IEnumerable<string> VideoExtensions() => [".relink-video"];
        public IEnumerable<string> AudioExtensions() => [".relink-audio"];

        public MediaReader Open(string file, MediaOptions options)
        {
            OpenCount++;
            var reader = new Moq.Mock<MediaReader>();
            reader.SetupGet(value => value.HasVideo).Returns(options.StreamsToLoad == MediaMode.Video);
            reader.SetupGet(value => value.HasAudio).Returns(options.StreamsToLoad == MediaMode.Audio);
            reader.SetupGet(value => value.VideoInfo).Returns(new VideoStreamInfo("test", new Rational(2, 1),
                new Beutl.Media.PixelSize(2, 2), new Rational(30, 1)));
            reader.SetupGet(value => value.AudioInfo).Returns(new AudioStreamInfo("test", new Rational(2, 1), 44100, 2));
            return reader.Object;
        }
    }

    [AvaloniaTest]
    [TestCase(false, 980, 620)]
    [TestCase(true, 980, 620)]
    [TestCase(false, 760, 440)]
    public async Task Dialog_renders_virtualized_rows_and_allows_invalid_files_to_stay_offline(bool dark, int width, int height)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await CreateEditorAsync();
        string directory = NewDirectory();
        using (editor.HistoryManager.SuppressRecording())
        {
            for (int i = 0; i < 350; i++)
                AddImage(editor.Scene, Path.Combine(directory, "previous-media-folder", $"footage-{i:D3}.png"), $"Shot {i:D3}");
        }
        using var vm = new MissingMediaDialogViewModel(editor);
        var dialog = new MissingMediaDialog
        { DataContext = vm, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            dialog.Show();
            HeadlessTestHelpers.Render(3);
            var realized = dialog.FindControl<ListBox>("MissingMediaList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.That(realized, Has.Length.LessThan(40));
            var firstRow = realized[0];
            var texts = firstRow.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().ToArray();
            var kindText = texts.Single(text => text.Name == "MediaKindText");
            var nameText = texts.Single(text => text.Name == "MediaNameText");
            var pathText = texts.Single(text => text.Name == "ExpectedPathText");
            Assert.That(nameText.TranslatePoint(default, firstRow)!.Value.X,
                Is.GreaterThanOrEqualTo(kindText.TranslatePoint(default, firstRow)!.Value.X + kindText.Bounds.Width + 8));
            Assert.That(pathText.Bounds.Width, Is.GreaterThan(200));
            Assert.That(dialog.FindControl<Button>("ApplyButton")!.IsEnabled, Is.False);
            string invalid = Path.Combine(directory, "unknown.png");
            File.WriteAllText(invalid, "not an image");
            await vm.SetReplacementAsync(vm.Rows[0], invalid);
            vm.Rows[0].IsOffline.Value = true;
            HeadlessTestHelpers.Render(3);
            Assert.That(vm.Rows[0].ReplacementPath.Value, Is.Null);
            Assert.That(vm.Error.Value, Is.Not.Null);
            Assert.That(dialog.FindControl<Button>("ContinueOfflineButton")!.IsEnabled, Is.True);
            Assert.That(dialog.FindControl<Button>("ApplyButton")!.IsEnabled, Is.False);
            using WriteableBitmap? frame = dialog.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            string output = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", $"missing-media-{(dark ? "dark" : "light")}-{width}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            frame!.Save(output, PngBitmapEncoderOptions.Default);
            TestContext.Out.WriteLine($"Capture: {output}");
        }
        finally
        {
            dialog.Close();
            await TestReset.ResetShellAsync();
        }
    }

    private static async Task<EditViewModel> CreateEditorAsync()
    {
        string name = "relink-" + Guid.NewGuid().ToString("N");
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, location))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }
}
