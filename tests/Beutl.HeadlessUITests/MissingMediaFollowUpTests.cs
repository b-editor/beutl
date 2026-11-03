using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.MissingMediaTab.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dock;

namespace Beutl.HeadlessUITests;

public partial class MissingMediaTests
{
    [AvaloniaTest]
    public async Task Ordinary_edits_do_not_schedule_media_reads_but_reference_edits_do()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory(); string path = Path.Combine(directory, "image.png");
        File.WriteAllBytes(path, s_png);
        var element = AddImage(editor.Scene, path);
        editor.HistoryManager.Commit();
        await editor.WaitForMediaFingerprintsAsync(); await editor.WaitForMissingMediaAsync();
        int captures = 0;
        editor.CaptureMediaFingerprints = (_, _) => { captures++; return Task.CompletedTask; };
        try
        {
            element.Name.Value = "Renamed clip"; element.Start.Value = MediaTime.FromSeconds(2);
            editor.HistoryManager.Commit();
            await editor.WaitForMediaFingerprintsAsync(); await editor.WaitForMissingMediaAsync();
            Assert.That(captures, Is.Zero);
            ((SourceImage)element.Objects.Single()).Source.Value = ImageSource.Open(Path.Combine(directory, "missing.png"));
            editor.HistoryManager.Commit();
            await editor.WaitForMediaFingerprintsAsync();
            Assert.That(captures, Is.EqualTo(1));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Ordinary_hash_scans_reuse_completed_hashes_and_forced_checks_reread_bytes()
    {
        string path = Path.Combine(NewDirectory(), "image.png"); File.WriteAllBytes(path, s_png);
        var scene = new Scene(); AddImage(scene, path);
        var service = new MissingMediaService(); await service.UpdateFingerprintsAsync(scene);
        string previous = scene.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256;
        DateTime time = File.GetLastWriteTimeUtc(path);
        byte[] changed = s_png.ToArray(); changed[^1] ^= 1;
        File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, time);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await service.UpdateFingerprintsAsync(scene, force: false);
            Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256, Is.EqualTo(previous));
        }
        await service.UpdateFingerprintsAsync(scene, force: true);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256, Is.Not.EqualTo(previous));
    }

    [AvaloniaTest]
    public async Task Ordinary_missing_checks_reuse_validations_but_refresh_rechecks_readability()
    {
        string directory = NewDirectory(); string path = Path.Combine(directory, "image.png");
        File.WriteAllBytes(path, s_png);
        var scene = new Scene(); AddImage(scene, path);
        var service = new MissingMediaService();
        Assert.That(await service.FindMissingAsync(scene, revalidateExisting: false), Is.Empty);
        // Native decoders need not honor .NET sharing flags on Unix. Changed
        // bytes exercise the cached check and explicit refresh on every platform.
        File.WriteAllText(path, "This is no longer a readable image.");
        AddImage(scene, Path.Combine(directory, "missing.png"));
        Assert.That((await service.FindMissingAsync(scene, revalidateExisting: false)).Select(item => item.Name),
            Is.EqualTo(new[] { "missing.png" }));
        Assert.That((await service.FindMissingAsync(scene)).Select(item => item.Name),
            Is.EquivalentTo(new[] { "image.png", "missing.png" }));
    }

    private static Element AddVideo(Scene scene, string path, MediaTime start = default)
    {
        var element = new Element { Start = { Value = start }, Length = { Value = MediaTime.FromSeconds(3) } };
        if (scene.Uri != null) element.Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri.LocalPath)!, $"{element.Id.Value}.belm"));
        element.Objects.Add(new SourceVideo { Source = { Value = VideoSource.Open(path) } });
        scene.Children.Add(element);
        return element;
    }

    [AvaloniaTest]
    public async Task A_check_decodes_a_file_again_only_after_it_changes()
    {
        var decoder = new RestoredMediaDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            string path = Path.Combine(NewDirectory(), "clip.relink-video");
            File.WriteAllText(path, "video");
            var scene = new Scene(); AddVideo(scene, path);
            var service = new MissingMediaService();
            Assert.That(await service.FindMissingAsync(scene), Is.Empty);
            Assert.That(await service.FindMissingAsync(scene), Is.Empty);
            Assert.That(decoder.OpenCount, Is.EqualTo(1));
            File.WriteAllText(path, "changed video");
            Assert.That(await service.FindMissingAsync(scene), Is.Empty);
            Assert.That(decoder.OpenCount, Is.EqualTo(2));
            File.Delete(path);
            Assert.That((await service.FindMissingAsync(scene)).Single().Name, Is.EqualTo("clip.relink-video"));
        }
        finally { DecoderRegistry.Unregister(decoder); }
    }

    [AvaloniaTest]
    public async Task A_check_reports_a_file_that_became_unreadable_with_the_same_size_and_time()
    {
        string path = Path.Combine(NewDirectory(), "image.png"); File.WriteAllBytes(path, s_png);
        var scene = new Scene(); AddImage(scene, path);
        var service = new MissingMediaService();
        Assert.That(await service.FindMissingAsync(scene), Is.Empty);
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.That((await service.FindMissingAsync(scene)).Single().Name, Is.EqualTo("image.png"));
        }
        else
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.None);
            try
            {
                try
                {
                    File.OpenRead(path).Dispose();
                    Assert.Ignore("This account can read files without permission bits.");
                }
                catch (UnauthorizedAccessException) { }
                Assert.That((await service.FindMissingAsync(scene)).Single().Name, Is.EqualTo("image.png"));
            }
            finally { File.SetUnixFileMode(path, mode); }
        }
    }

    [AvaloniaTest]
    public async Task A_repair_decodes_only_its_replacement_and_only_once()
    {
        await TestReset.ResetShellAsync();
        var decoder = new RestoredMediaDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            var editor = await CreateEditorAsync();
            string directory = NewDirectory();
            string present = Path.Combine(directory, "present.relink-video"); File.WriteAllText(present, "present");
            string replacement = Path.Combine(directory, "replacement.relink-video"); File.WriteAllText(replacement, "replacement");
            using (editor.HistoryManager.SuppressRecording())
            {
                // Away from the playhead, so that rendering the preview opens neither.
                MediaTime start = MediaTime.FromSeconds(100);
                AddVideo(editor.Scene, present, start);
                AddVideo(editor.Scene, Path.Combine(directory, "missing.relink-video"), start);
            }
            await editor.OpenMissingMediaAsync();
            var tool = editor.FindToolTab<MissingMediaViewModel>()!;
            int opened = decoder.OpenCount;
            await tool.SetReplacementAsync(tool.Rows.Single(), replacement);
            await tool.WaitForCandidatesAsync();
            Assert.That(await tool.ApplyAsync(), Is.True, tool.Error.Value);
            Assert.That(tool.Rows, Is.Empty);
            Assert.That(decoder.OpenCount - opened, Is.EqualTo(1));
        }
        finally
        {
            await TestReset.ResetShellAsync();
            DecoderRegistry.Unregister(decoder);
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Choosing_a_file_for_one_kind_also_replaces_the_other_kinds_of_the_same_file(bool fileLacksTheOtherKind)
    {
        await TestReset.ResetShellAsync();
        var decoder = new RestoredMediaDecoder { VideoOnly = fileLacksTheOtherKind };
        DecoderRegistry.Register(decoder);
        try
        {
            var editor = await CreateEditorAsync();
            string directory = NewDirectory();
            string missing = Path.Combine(directory, "missing.relink-video");
            string replacement = Path.Combine(directory, "replacement.relink-video"); File.WriteAllText(replacement, "replacement");
            using (editor.HistoryManager.SuppressRecording())
            {
                AddVideo(editor.Scene, missing, MediaTime.FromSeconds(100)).Objects
                    .Add(new SourceSound { Source = { Value = SoundSource.Open(missing) } });
            }
            using var vm = new MissingMediaViewModel(editor);
            var searched = new List<MissingMediaKind>();
            vm.FindCandidateMatches = (items, _, _) =>
            {
                searched.AddRange(items.Select(item => item.Kind));
                return Task.FromResult<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>>(
                    new Dictionary<MissingMedia, IReadOnlyList<string>>());
            };
            MissingMediaRowViewModel video = vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Video);
            MissingMediaRowViewModel sound = vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Sound);
            string candidate = Path.Combine(directory, "candidate.relink-video");
            sound.CandidatePath.Value = candidate;

            await vm.SetReplacementAsync(video, replacement);
            await vm.WaitForCandidatesAsync();

            Assert.Multiple(() =>
            {
                Assert.That(video.ReplacementPath.Value, Is.EqualTo(replacement));
                Assert.That(sound.ReplacementPath.Value, Is.EqualTo(fileLacksTheOtherKind ? null : replacement));
                Assert.That(sound.CandidatePath.Value, Is.EqualTo(fileLacksTheOtherKind ? candidate : null));
                // Only a row still without a file is left for the search, which reads whole files.
                Assert.That(searched, fileLacksTheOtherKind ? Is.EqualTo(new[] { MissingMediaKind.Sound }) : Is.Empty);
                Assert.That(sound.Status.Value, Is.EqualTo(fileLacksTheOtherKind ? MissingMediaStrings.Missing : MissingMediaStrings.Ready));
                Assert.That(vm.Error.Value, Is.Null);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
            DecoderRegistry.Unregister(decoder);
        }
    }

    [AvaloniaTest]
    public async Task A_repair_leaves_the_editor_and_its_tab_free_during_the_snapshot()
    {
        await TestReset.ResetShellAsync();
        try
        {
            var editor = await CreateEditorAsync();
            string directory = NewDirectory();
            string replacement = Path.Combine(directory, "replacement.png"); File.WriteAllBytes(replacement, s_png);
            using (editor.HistoryManager.SuppressRecording()) AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
            var snapshot = new TaskCompletionSource();
            CancellationToken snapshotToken = default;
            var session = new Moq.Mock<IProjectVersionControlSession>();
            session.Setup(value => value.NotifySavedAsync(Moq.It.IsAny<IProjectFileWriteLease?>(), Moq.It.IsAny<CancellationToken>()))
                .Callback((IProjectFileWriteLease? _, CancellationToken token) => snapshotToken = token)
                .Returns(snapshot.Task);
            var context = new Moq.Mock<IEditorContext>();
            context.Setup(value => value.GetService(Moq.It.IsAny<Type>())).Returns((Type type)
                => type == typeof(IProjectVersionControlCoordinator) ? session.Object : editor.GetService(type));
            using var vm = new MissingMediaViewModel(context.Object);
            Assert.That(editor.OpenToolTab(vm), Is.True);
            await vm.SetReplacementAsync(vm.Rows.Single(), replacement);
            await vm.WaitForCandidatesAsync();

            Task<bool> apply = vm.ApplyAsync();
            bool enabled, busy, rowsLeft, completed, closable, canceledByClosing;
            try
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => session.Invocations.Count > 0);
                (enabled, busy, rowsLeft, completed) = (editor.IsEnabled.Value, vm.IsBusy.Value, vm.Rows.Count > 0, apply.IsCompleted);
                closable = editor.DockHost.Factory.EnumerateTools().Single(item => item.ToolContext == vm).CanClose;
                editor.CloseToolTab(vm);
                canceledByClosing = snapshotToken.IsCancellationRequested;
            }
            finally { snapshot.SetResult(); }

            Assert.That(await apply, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(enabled, Is.True);
                Assert.That(busy, Is.False);
                Assert.That(rowsLeft, Is.False);
                Assert.That(completed, Is.False);
                Assert.That(closable, Is.True);
                Assert.That(canceledByClosing, Is.False);
            });
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Saving_replaces_changed_element_files_only_after_writing_them_all()
    {
        await TestReset.ResetShellAsync();
        try
        {
            var editor = await CreateEditorAsync();
            string image = Path.Combine(NewDirectory(), "image.png"); File.WriteAllBytes(image, s_png);
            var elements = new List<Element>();
            using (editor.HistoryManager.SuppressRecording())
            {
                for (int i = 0; i < 64; i++) elements.Add(AddImage(editor.Scene, image, $"Clip {i}"));
            }
            Assert.That(await editor.SaveAsync(), Is.True);
            using (editor.HistoryManager.SuppressRecording())
            {
                foreach (Element element in elements) element.Name.Value += " renamed";
            }

            string folder = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
            int? writtenAtFirstReplacement = null;
            using (StorageWriteTransaction.InjectFaultsForTesting((step, target) =>
                   {
                       if (step == StorageWriteStep.Replace && target.EndsWith(".belm", StringComparison.Ordinal))
                           writtenAtFirstReplacement ??= Directory.GetFiles(folder, "*.tmp").Length;
                   }))
            {
                Assert.That(await editor.SaveAsync(), Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(writtenAtFirstReplacement, Is.EqualTo(64));
                Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
                Assert.That(CoreSerializer.RestoreFromUri<Element>(elements[63].Uri!).Name.Value, Is.EqualTo("Clip 63 renamed"));
            });
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Refreshing_the_repair_tab_does_not_walk_the_scene_again()
    {
        await TestReset.ResetShellAsync();
        try
        {
            var editor = await CreateEditorAsync();
            string path = Path.Combine(NewDirectory(), "image.png"); File.WriteAllBytes(path, s_png);
            using (editor.HistoryManager.SuppressRecording())
            {
                for (int i = 0; i < 1000; i++) AddImage(editor.Scene, path);
            }
            await editor.OpenMissingMediaAsync();
            var tool = editor.FindToolTab<MissingMediaViewModel>()!;

            // A walk of these elements allocates about 10 MiB on this thread.
            long before = GC.GetAllocatedBytesForCurrentThread();
            await editor.OpenMissingMediaAsync(refresh: true);
            long afterOpen = GC.GetAllocatedBytesForCurrentThread();
            // The references are gathered before the first await.
            Task rows = tool.RefreshRowsAsync();
            long afterRows = GC.GetAllocatedBytesForCurrentThread();
            await rows;
            Assert.Multiple(() =>
            {
                Assert.That(afterOpen - before, Is.LessThan(5 * 1024 * 1024));
                Assert.That(afterRows - afterOpen, Is.LessThan(5 * 1024 * 1024));
            });
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public void Saving_a_scene_with_no_temporary_storage_resolves_no_paths()
    {
        string directory = NewDirectory();
        var scene = new Scene { Uri = new Uri(Path.Combine(directory, "main.scene")) };
        for (int i = 0; i < 1000; i++) AddImage(scene, Path.Combine(directory, "image.png"));

        // Resolving the paths of these elements and their sources allocates about 40 MiB.
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = new UnsavedSceneStorage.SaveRelocation(scene, scene.Uri);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.LessThan(5 * 1024 * 1024));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Applying_a_font_copies_complementary_faces(bool selectImage, bool waitForCandidates)
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        string directory = NewDirectory(); var files = new List<string>();
        foreach (string name in new[] { "ProjectFontSecondFixture.ttf", "ProjectFontMediumFixture.ttf" })
        {
            string path = Path.Combine(directory, name); File.WriteAllBytes(path, RelinkFont(name)); files.Add(path);
        }
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock
            {
                FontFamily = { Value = new FontFamily("Relink") },
                FontWeight = { Value = FontWeight.Medium }
            });
            if (selectImage) AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
        }
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Font), files[0]);
            if (selectImage)
            {
                string image = Path.Combine(NewDirectory(), "image.png"); File.WriteAllBytes(image, s_png);
                await vm.SetReplacementAsync(vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Image), image);
            }
            // The UI's immediate Apply path does not wait for candidate discovery.
            if (waitForCandidates)
                await vm.WaitForCandidatesAsync();
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(vm.Rows, Is.Empty);
            Assert.That(FontManager.Instance.GetTypefaces(new FontFamily("Relink")).Select(face => face.Weight),
                Is.EquivalentTo(new[] { FontWeight.Regular, FontWeight.Medium }));
            string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
            string bundled = Path.Combine(Path.GetDirectoryName(projectPath)!, "resources", "fonts");
            Assert.That(Directory.GetFiles(bundled), Has.Length.EqualTo(2));
            await TestReset.ResetShellAsync(); await TestShell.Project.OpenProject(projectPath);
            Assert.That(FontManager.Instance.GetTypefaces(new FontFamily("Relink")).Select(face => face.Weight),
                Is.EquivalentTo(new[] { FontWeight.Regular, FontWeight.Medium }));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Font_candidates_in_sibling_directories_keep_complementary_faces_after_apply()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        string directory = NewDirectory(); var files = new List<string>();
        foreach (string name in new[] { "ProjectFontSecondFixture.ttf", "ProjectFontMediumFixture.ttf" })
        {
            string folder = Directory.CreateDirectory(Path.Combine(directory,
                name == "ProjectFontSecondFixture.ttf" ? "a-regular" : "b-medium")).FullName;
            string path = Path.Combine(folder, name); File.WriteAllBytes(path, RelinkFont(name)); files.Add(path);
        }
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock
            {
                FontFamily = { Value = new FontFamily("Relink") },
                FontWeight = { Value = FontWeight.Medium }
            });
            AddImage(editor.Scene, Path.Combine(directory, "old", "seed.png"));
        }
        string image = Path.Combine(directory, "seed.png"); File.WriteAllBytes(image, s_png);
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Image), image);
            await vm.WaitForCandidatesAsync();
            var font = vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Font);
            Assert.That(font.FontReplacementFiles, Is.EquivalentTo(files));
            vm.UseCandidate(font);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
            string bundled = Path.Combine(Path.GetDirectoryName(projectPath)!, "resources", "fonts");
            Assert.That(Directory.GetFiles(bundled), Has.Length.EqualTo(2));
            await TestReset.ResetShellAsync(); await TestShell.Project.OpenProject(projectPath);
            Assert.That(FontManager.Instance.GetTypefaces(new FontFamily("Relink")).Select(face => face.Weight),
                Is.EquivalentTo(new[] { FontWeight.Regular, FontWeight.Medium }));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task A_previous_folder_search_cannot_replace_a_new_manual_choice()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        using (editor.HistoryManager.SuppressRecording())
        {
            AddImage(editor.Scene, Path.Combine(directory, "old", "seed.png"));
            AddImage(editor.Scene, Path.Combine(directory, "old", "other.png"));
        }
        foreach (string name in new[] { "seed.png", "old-candidate.png", "chosen.png" }) File.WriteAllBytes(Path.Combine(directory, name), s_png);
        var matches = new TaskCompletionSource<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>>();
        var started = new TaskCompletionSource(); var canceled = new TaskCompletionSource();
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            vm.FindCandidateMatches = (_, _, token) => { token.Register(() => canceled.TrySetResult()); started.TrySetResult(); return matches.Task; };
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Name == "seed.png"), Path.Combine(directory, "seed.png"));
            await started.Task;
            var row = vm.Rows.Single(item => item.Name == "other.png");
            Task selection = vm.SetReplacementAsync(row, Path.Combine(directory, "chosen.png"));
            Assert.That(canceled.Task.IsCompleted, Is.True);
            matches.SetResult(new Dictionary<MissingMedia, IReadOnlyList<string>> { [row.Media] = [Path.Combine(directory, "old-candidate.png")] });
            await selection; await vm.WaitForCandidatesAsync();
            Assert.That(row.CandidatePath.Value, Is.Null);
            vm.UseAllCandidates();
            Assert.That(row.ReplacementPath.Value, Is.EqualTo(Path.Combine(directory, "chosen.png")));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task User_dismissed_warnings_stay_closed_until_the_scene_is_healthy_again()
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        var previous = NotificationService.Handler; var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            string directory = NewDirectory(); var first = AddImage(editor.Scene, Path.Combine(directory, "first.png"));
            editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            handler.Notifications.Single().OnClose!();
            first.Name.Value = "Offline clip"; editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            var second = AddImage(editor.Scene, Path.Combine(directory, "second.png"));
            editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            Assert.That(handler.Notifications, Has.Count.EqualTo(1));
            string valid = Path.Combine(directory, "valid.png"); File.WriteAllBytes(valid, s_png);
            foreach (var element in new[] { first, second }) ((SourceImage)element.Objects.Single()).Source.Value = ImageSource.Open(valid);
            editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            AddImage(editor.Scene, Path.Combine(directory, "new-missing.png"));
            editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            Assert.That(handler.Notifications, Has.Count.EqualTo(2));
        }
        finally { await TestReset.ResetShellAsync(); NotificationService.Handler = previous; }
    }

    [AvaloniaTest]
    [TestCase("texture.png")]
    [TestCase("surface.mtl")]
    public async Task Models_with_missing_optional_bundle_members_remain_repairable(string missingFile)
    {
        string directory = NewDirectory(); string path = Path.Combine(directory, "model.obj");
        File.WriteAllBytes(Path.Combine(directory, "texture.png"), s_png);
        File.WriteAllText(Path.Combine(directory, "surface.mtl"), "newmtl surface\nmap_Kd texture.png\n");
        File.WriteAllText(path, "mtllib surface.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl surface\nf 1 2 3\n");
        var source = new ModelSource(); source.ReadFrom(new Uri(path));
        var scene = new Scene(); var element = AddImage(scene, Path.Combine(directory, "unused.png"));
        element.Objects.Clear(); element.Objects.Add(new Model3D { Source = { Value = source } });
        var service = new MissingMediaService(); await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Dependencies!.Keys, Does.Contain(missingFile));
        File.Delete(Path.Combine(directory, missingFile));
        source.ReadFrom(new Uri(path)); Assert.That(source.MeshCount, Is.EqualTo(1));
        await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Dependencies!.Keys, Does.Contain(missingFile));
        Assert.That((await service.FindMissingAsync(scene)).Any(item => item.Kind == MissingMediaKind.Model), Is.True);
    }

    // Applying a repair holds its tab open through the dock, so the tab cannot be closed mid-repair.
    [AvaloniaTest]
    public async Task The_dock_keeps_the_repair_tab_open_until_released()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        try
        {
            var tab = new MissingMediaViewModel(editor, []);
            Assert.That(editor.OpenToolTab(tab), Is.True);
            var dock = (IToolDockHost)editor.GetService(typeof(IToolDockHost))!;
            BeutlToolDockable dockable = editor.DockHost.Factory.EnumerateTools().Single(tool => tool.ToolContext == tab);
            using (dock.KeepOpen(tab))
                Assert.That(dockable.CanClose, Is.False);
            Assert.That(dockable.CanClose, Is.True);
            using var closed = new MissingMediaViewModel(editor, []);
            Assert.DoesNotThrow(() => dock.KeepOpen(closed).Dispose());
        }
        finally { await TestReset.ResetShellAsync(); }
    }
}
