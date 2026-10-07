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
        await editor.WaitForMediaFingerprintsAsync();
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

    [AvaloniaTest]
    public async Task A_failed_selection_clears_the_previous_ready_replacement()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        using (editor.HistoryManager.SuppressRecording()) AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
        string valid = Path.Combine(directory, "valid.png");
        string invalid = Path.Combine(directory, "invalid.png");
        File.WriteAllBytes(valid, s_png);
        File.WriteAllText(invalid, "not an image");
        using var vm = new MissingMediaDialogViewModel(editor);
        await vm.SetReplacementAsync(vm.Rows.Single(), valid);
        Assert.That(vm.CanApply.Value, Is.True);
        await vm.SetReplacementAsync(vm.Rows.Single(), invalid);
        Assert.That(vm.Rows.Single().ReplacementPath.Value, Is.Null);
        Assert.That(vm.CanApply.Value, Is.False);
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task Fingerprinted_files_match_after_their_extension_is_changed()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "original.jpeg");
        File.WriteAllBytes(path, s_png);
        var scene = new Scene(); AddImage(scene, path);
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene);
        string renamed = Path.Combine(directory, "renamed.jpg");
        File.Move(path, renamed);
        var missing = service.FindMissing(scene).Single();
        Assert.That((await service.FindMatchesAsync([missing], directory))[missing], Is.EqualTo(renamed));
        await service.ValidateAsync(missing, renamed);
    }

    [AvaloniaTest]
    public async Task Relinking_one_media_kind_retains_the_hash_for_the_remaining_offline_reference()
    {
        string directory = NewDirectory();
        string original = Path.Combine(directory, "video.mp4");
        File.WriteAllText(original, "original");
        var scene = new Scene();
        var element = AddImage(scene, Path.Combine(directory, "missing.png")); element.Objects.Clear();
        var video = VideoSource.Open(original); var sound = SoundSource.Open(original);
        element.Objects.Add(new SourceVideo { Source = { CurrentValue = video } });
        element.Objects.Add(new SourceSound { Source = { CurrentValue = sound } });
        var service = new MissingMediaService();
        await service.UpdateFingerprintsAsync(scene);
        string replacement = Path.Combine(directory, "different.mp4");
        File.WriteAllText(replacement, "replaced");
        File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(original));
        var old = scene.MediaFingerprints[video.Uri.AbsoluteUri];
        File.Delete(original);
        ResourceRelocationService.RelinkFileSource(video, new Uri(replacement));
        Assert.That(scene.MediaFingerprints.ContainsKey(sound.Uri.AbsoluteUri), Is.True);
        await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[sound.Uri.AbsoluteUri], Is.EqualTo(old));
        Assert.That(scene.MediaFingerprints[video.Uri.AbsoluteUri].Sha256, Is.Not.EqualTo(old.Sha256));
    }

    [AvaloniaTest]
    public async Task Saving_does_not_wait_for_hashing_or_persist_unsaved_edits_when_hashing_finishes()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        string directory = NewDirectory(); string path = Path.Combine(directory, "image.png");
        File.WriteAllBytes(path, s_png);
        using (editor.HistoryManager.SuppressRecording()) AddImage(editor.Scene, path);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        editor.CaptureMediaFingerprints = async (scene, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            await new MissingMediaService().UpdateFingerprintsAsync(scene, token);
        };
        try
        {
            ValueTask<bool> save = editor.SaveAsync();
            Assert.That(save.IsCompletedSuccessfully, Is.True, "Saving must not depend on a pending media read.");
            await save;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using (editor.HistoryManager.SuppressRecording()) editor.Scene.Duration = TimeSpan.FromSeconds(17);
            release.SetResult();
            await editor.WaitForMediaFingerprintsAsync();
            var reopened = CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!);
            Assert.That(reopened.Duration, Is.Not.EqualTo(TimeSpan.FromSeconds(17)));
            Assert.That(reopened.MediaFingerprints.ContainsKey(new Uri(path).AbsoluteUri), Is.True);
        }
        finally { release.TrySetResult(); await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Unsaved_removal_or_relink_keeps_the_saved_reference_fingerprint(bool relink)
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        string path = Path.Combine(NewDirectory(), "original.png");
        File.WriteAllBytes(path, s_png);
        Element element;
        using (editor.HistoryManager.SuppressRecording()) element = AddImage(editor.Scene, path);
        await new MissingMediaService().UpdateFingerprintsAsync(editor.Scene);
        var fingerprint = editor.Scene.MediaFingerprints[new Uri(path).AbsoluteUri];
        await editor.SaveAsync();
        await editor.WaitForMediaFingerprintsAsync();
        byte[] savedBytes = File.ReadAllBytes(editor.Scene.Uri!.LocalPath);
        try
        {
            using (editor.HistoryManager.SuppressRecording())
            {
                if (relink)
                {
                    string replacement = Path.Combine(NewDirectory(), "replacement.png");
                    File.WriteAllBytes(replacement, [.. s_png, 1]);
                    ResourceRelocationService.RelinkFileSource(((SourceImage)element.Objects.Single()).Source.CurrentValue!, new Uri(replacement));
                }
                else editor.Scene.Children.Remove(element);
            }
            editor.ScheduleMediaFingerprints();
            await editor.WaitForMediaFingerprintsAsync();
            var saved = CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri);
            Assert.That(editor.Scene.MediaFingerprints.ContainsKey(new Uri(path).AbsoluteUri), Is.False);
            Assert.That(saved.MediaFingerprints[new Uri(path).AbsoluteUri], Is.EqualTo(fingerprint));
            Assert.That(File.ReadAllBytes(editor.Scene.Uri.LocalPath), Is.EqualTo(savedBytes));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Auto_saved_media_keeps_its_delayed_fingerprint_without_an_explicit_save()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        bool oldAutoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        editor.CaptureMediaFingerprints = async (scene, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            await new MissingMediaService().UpdateFingerprintsAsync(scene, token);
        };
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = false;
            string directory = NewDirectory(); string path = Path.Combine(directory, "original.png");
            File.WriteAllBytes(path, s_png);
            AddImage(editor.Scene, path);
            editor.HistoryManager.Commit("Add media");
            HeadlessTestHelpers.Settle();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!).Children, Has.Count.EqualTo(1));
            release.SetResult();
            await editor.WaitForMediaFingerprintsAsync();
            string renamed = Path.Combine(directory, "renamed.png"); File.Move(path, renamed);
            var saved = CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!);
            var service = new MissingMediaService();
            var missing = service.FindMissing(saved).Single();
            Assert.That(missing.Fingerprint, Is.Not.Null);
            Assert.That((await service.FindMatchesAsync([missing], directory))[missing], Is.EqualTo(renamed));
        }
        finally
        {
            release.TrySetResult();
            await TestReset.ResetShellAsync();
            EditViewModel.IsAutoSaveSuppressedForTesting = oldAutoSave;
        }
    }

    [AvaloniaTest]
    public async Task Applying_repairs_notifies_the_active_version_control_session()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory(); string path = Path.Combine(directory, "replacement.png");
        File.WriteAllBytes(path, s_png);
        using (editor.HistoryManager.SuppressRecording()) AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
        var old = editor.EditorService.ProjectVersionControlCoordinator;
        var coordinator = new Moq.Mock<Beutl.Editor.VersionControl.IProjectVersionControlCoordinator>();
        var session = coordinator.As<Beutl.Editor.VersionControl.IProjectVersionControlSession>();
        session.Setup(value => value.NotifySavedAsync(Moq.It.IsAny<Beutl.Editor.VersionControl.IProjectFileWriteLease>(), Moq.It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        editor.EditorService.ProjectVersionControlCoordinator = coordinator.Object;
        try
        {
            using var vm = new MissingMediaDialogViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(), path);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            await editor.WaitForMediaFingerprintsAsync();
            session.Verify(value => value.NotifySavedAsync(Moq.It.Is<Beutl.Editor.VersionControl.IProjectFileWriteLease>(lease => lease != null), Moq.It.IsAny<CancellationToken>()), Moq.Times.Exactly(2));
        }
        finally { editor.EditorService.ProjectVersionControlCoordinator = old; await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Explicit_relink_refreshes_files_restored_before_the_scan()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        string path = Path.Combine(NewDirectory(), "restored.png");
        using (editor.HistoryManager.SuppressRecording()) AddImage(editor.Scene, path);
        var compositor = editor.Renderer.Value.Compositor;
        var resource = (SourceImage.Resource)compositor.EvaluateGraphics(TimeSpan.Zero).Objects.Single();
        Assert.That(resource.Source!.IsOffline, Is.True);
        File.WriteAllBytes(path, s_png);
        var owner = new Window(); owner.Show();
        try
        {
            await editor.ShowMissingMediaAsync(owner);
            var refreshed = (SourceImage.Resource)compositor.EvaluateGraphics(TimeSpan.Zero).Objects.Single();
            Assert.That(refreshed.Source!.IsOffline, Is.False);
        }
        finally { owner.Close(); await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Replacement_model_updates_geometry_while_preserving_materials_and_child_ids()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        string directory = NewDirectory(); string original = Path.Combine(directory, "old.obj");
        string replacement = Path.Combine(directory, "new.obj");
        File.WriteAllText(original, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        File.WriteAllText(replacement, "v 0 0 0\nv 3 0 0\nv 0 2 0\nf 1 2 3\n");
        var source = new ModelSource(); source.ReadFrom(new Uri(original));
        var model = new Model3D { Source = { CurrentValue = source } };
        var child = (MeshObject3D)model.Children.Single();
        child.Name = "User mesh"; var material = child.Material.CurrentValue;
        var mesh = (ModelMesh)child.Mesh.CurrentValue!;
        var oldVertices = mesh.Vertices.CurrentValue;
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(model);
        }
        await new MissingMediaService().UpdateFingerprintsAsync(editor.Scene);
        File.Delete(original);
        try
        {
            using var vm = new MissingMediaDialogViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(), replacement);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(model.Children.Single(), Is.SameAs(child));
            Assert.That(child.Name, Is.EqualTo("User mesh"));
            Assert.That(child.Material.CurrentValue, Is.SameAs(material));
            Assert.That(((ModelMesh)child.Mesh.CurrentValue!).Vertices.CurrentValue, Is.Not.EqualTo(oldVertices));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Repairing_a_moved_model_preserves_reordered_and_added_saved_children(bool withFingerprint)
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        string directory = NewDirectory(); string original = Path.Combine(directory, "shapes.obj");
        File.WriteAllText(original, "o First\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\no Second\nv 3 0 0\nv 5 0 0\nv 3 2 0\nf 4 5 6\n");
        var source = new ModelSource(); source.ReadFrom(new Uri(original));
        var model = new Model3D { Source = { CurrentValue = source } };
        Assert.That(model.Children, Has.Count.EqualTo(2));
        var first = model.Children[0]; model.Children.RemoveAt(0); model.Children.Add(first);
        var mesh = (ModelMesh)((MeshObject3D)first).Mesh.CurrentValue!;
        model.Children.Add(new MeshObject3D
        {
            Name = "Added mesh",
            Mesh = { CurrentValue = new ModelMesh
            { Vertices = { CurrentValue = mesh.Vertices.CurrentValue }, Indices = { CurrentValue = mesh.Indices.CurrentValue } } }
        });
        first.Name = "Edited first"; first.Position.CurrentValue = new System.Numerics.Vector3(7, 8, 9);
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(model);
        }
        if (withFingerprint) await new MissingMediaService().UpdateFingerprintsAsync(editor.Scene);
        string moved = Path.Combine(NewDirectory(), "shapes.obj"); File.Move(original, moved);
        await editor.SaveAsync(); await editor.WaitForMediaFingerprintsAsync();
        string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
        await TestReset.ResetShellAsync(); await TestShell.Project.OpenProject(projectPath);
        TestShell.Editor.ActivateTabItem(TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().Single());
        editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var restored = (Model3D)editor.Scene.Children.Single().Objects.Single();
        var children = restored.Children.ToArray();
        var savedChildren = children.Select(child => CoreSerializer.SerializeToJsonObject(child).ToJsonString()).ToArray();
        try
        {
            using var vm = new MissingMediaDialogViewModel(editor);
            if (withFingerprint) await vm.FindInDirectoryAsync(Path.GetDirectoryName(moved)!);
            else await vm.SetReplacementAsync(vm.Rows.Single(), moved);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(restored.Children, Is.EqualTo(children));
            Assert.That(restored.Children.Select(child => CoreSerializer.SerializeToJsonObject(child).ToJsonString()), Is.EqualTo(savedChildren));
            var persisted = (Model3D)CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!).Children.Single().Objects.Single();
            Assert.That(persisted.Children.Select(child => CoreSerializer.SerializeToJsonObject(child).ToJsonString()), Is.EqualTo(savedChildren));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    [TestCase("zh-CN")]
    [TestCase("ko-KR")]
    [TestCase("es")]
    public void Dialog_strings_have_translations_for_every_supported_culture(string culture)
    {
        var manager = new System.Resources.ResourceManager("Beutl.Language.MissingMediaStrings", typeof(Beutl.Language.MissingMediaStrings).Assembly);
        var resources = manager.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo(culture), true, false);
        Assert.That(resources, Is.Not.Null);
        Assert.That(resources!.GetString("Title"), Is.Not.EqualTo("Relink missing media"));
        Assert.That(resources.GetString("Unrecognized"), Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task A_font_collection_matches_and_loads_a_family_at_a_nonzero_index()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        byte[] FontBytes(string name)
        {
            using var input = typeof(MissingMediaTests).Assembly.GetManifestResourceStream(name)!;
            using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
        }
        byte[][] faces = [FontBytes("ProjectFontSecondFixture.ttf"), FontBytes("ProjectFontFixture.ttf")];
        int[] offsets = [20, (20 + faces[0].Length + 3) & ~3];
        byte[] collection = new byte[offsets[1] + faces[1].Length];
        "ttcf"u8.CopyTo(collection);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x10000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), 2);
        for (int index = 0; index < 2; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12 + index * 4), (uint)offsets[index]);
            byte[] face = faces[index];
            int tables = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(face.AsSpan(4));
            for (int table = 0; table < tables; table++)
            {
                Span<byte> entry = face.AsSpan(12 + table * 16, 16);
                uint offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry[8..], offset + (uint)offsets[index]);
            }
            face.CopyTo(collection, offsets[index]);
        }
        string path = Path.Combine(NewDirectory(), "families.ttc"); File.WriteAllBytes(path, collection);
        Beutl.Media.FontFamily firstFamily;
        using (var first = SkiaSharp.SKTypeface.FromFile(path, 0))
        using (var second = SkiaSharp.SKTypeface.FromFile(path, 1))
        {
            Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.Null);
            Assert.That(first!.FamilyName, Is.Not.EqualTo(second!.FamilyName));
            firstFamily = new Beutl.Media.FontFamily(first.FamilyName);
        }
        var family = new Beutl.Media.FontFamily("Beutl Test Variable");
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(NewDirectory(), "missing.png")); element.Objects.Clear();
            element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock { FontFamily = { CurrentValue = family } });
        }
        try
        {
            using var vm = new MissingMediaDialogViewModel(editor);
            await vm.FindInDirectoryAsync(Path.GetDirectoryName(path)!);
            Assert.That(vm.Rows.Single().ReplacementPath.Value, Is.EqualTo(path));
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(FontManager.Instance.IsRegistered(family), Is.True);
            Assert.That(FontManager.Instance.IsRegistered(firstFamily), Is.True);
            var firstFaces = FontManager.Instance.GetTypefaces(firstFamily);
            var secondFaces = FontManager.Instance.GetTypefaces(family);
            string project = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
            await TestReset.ResetShellAsync(); await TestShell.Project.OpenProject(project);
            Assert.That(FontManager.Instance.IsRegistered(family), Is.True);
            Assert.That(FontManager.Instance.GetTypefaces(firstFamily), Is.EquivalentTo(firstFaces));
            Assert.That(FontManager.Instance.GetTypefaces(family), Is.EquivalentTo(secondFaces));
        }
        finally { await TestReset.ResetShellAsync(); }
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
