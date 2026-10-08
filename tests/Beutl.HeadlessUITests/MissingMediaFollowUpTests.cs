using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;

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
            element.Name = "Renamed clip"; element.Start = TimeSpan.FromSeconds(2);
            editor.HistoryManager.Commit();
            await editor.WaitForMediaFingerprintsAsync(); await editor.WaitForMissingMediaAsync();
            Assert.That(captures, Is.Zero);
            ((SourceImage)element.Objects.Single()).Source.CurrentValue = ImageSource.Open(Path.Combine(directory, "missing.png"));
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

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Applying_a_font_immediately_or_after_another_selection_copies_complementary_faces(bool selectImage)
    {
        await TestReset.ResetShellAsync(); var editor = await CreateEditorAsync();
        string directory = NewDirectory(); var files = new List<string>();
        foreach (string name in new[] { "ProjectFontSecondFixture.ttf", "ProjectFontMediumFixture.ttf" })
        {
            using var input = typeof(MissingMediaTests).Assembly.GetManifestResourceStream(name)!;
            using var output = new MemoryStream(); input.CopyTo(output); byte[] bytes = output.ToArray();
            foreach (var encoding in new[] { System.Text.Encoding.ASCII, System.Text.Encoding.BigEndianUnicode })
            {
                byte[] old = encoding.GetBytes("Roboto"); byte[] renamed = encoding.GetBytes("Relink");
                for (int i = 0; i <= bytes.Length - old.Length; i++)
                    if (bytes.AsSpan(i, old.Length).SequenceEqual(old)) renamed.CopyTo(bytes, i);
            }
            string path = Path.Combine(directory, name); File.WriteAllBytes(path, bytes); files.Add(path);
        }
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock
            {
                FontFamily = { CurrentValue = new FontFamily("Relink") },
                FontWeight = { CurrentValue = FontWeight.Medium }
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
            // Follow the UI's immediate Apply path; do not wait for candidate discovery.
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(FontManager.Instance.GetTypefaces(new FontFamily("Relink")).Select(face => face.Weight),
                Is.EquivalentTo(new[] { FontWeight.Regular, FontWeight.Medium }));
            string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
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
            using var input = typeof(MissingMediaTests).Assembly.GetManifestResourceStream(name)!;
            using var output = new MemoryStream(); input.CopyTo(output); byte[] bytes = output.ToArray();
            foreach (var encoding in new[] { System.Text.Encoding.ASCII, System.Text.Encoding.BigEndianUnicode })
            {
                byte[] old = encoding.GetBytes("Roboto"); byte[] renamed = encoding.GetBytes("Relink");
                for (int i = 0; i <= bytes.Length - old.Length; i++)
                    if (bytes.AsSpan(i, old.Length).SequenceEqual(old)) renamed.CopyTo(bytes, i);
            }
            string folder = Directory.CreateDirectory(Path.Combine(directory,
                name == "ProjectFontSecondFixture.ttf" ? "a-regular" : "b-medium")).FullName;
            string path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); files.Add(path);
        }
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png")); element.Objects.Clear();
            element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock
            {
                FontFamily = { CurrentValue = new FontFamily("Relink") },
                FontWeight = { CurrentValue = FontWeight.Medium }
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
            first.Name = "Offline clip"; editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            var second = AddImage(editor.Scene, Path.Combine(directory, "second.png"));
            editor.HistoryManager.Commit(); await editor.WaitForMissingMediaAsync();
            Assert.That(handler.Notifications, Has.Count.EqualTo(1));
            string valid = Path.Combine(directory, "valid.png"); File.WriteAllBytes(valid, s_png);
            foreach (var element in new[] { first, second }) ((SourceImage)element.Objects.Single()).Source.CurrentValue = ImageSource.Open(valid);
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
        element.Objects.Clear(); element.Objects.Add(new Model3D { Source = { CurrentValue = source } });
        var service = new MissingMediaService(); await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Dependencies!.Keys, Does.Contain(missingFile));
        File.Delete(Path.Combine(directory, missingFile));
        source.ReadFrom(new Uri(path)); Assert.That(source.MeshCount, Is.EqualTo(1));
        await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Dependencies!.Keys, Does.Contain(missingFile));
        Assert.That((await service.FindMissingAsync(scene)).Any(item => item.Kind == MissingMediaKind.Model), Is.True);
    }
}
