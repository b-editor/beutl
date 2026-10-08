using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics3D.Models;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Tools;

namespace Beutl.HeadlessUITests;

public partial class MissingMediaTests
{
    [AvaloniaTest]
    public async Task Selected_fonts_exclude_conflicting_and_unreadable_folder_matches()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        byte[] ReadFont(string name)
        {
            using var input = typeof(MissingMediaTests).Assembly.GetManifestResourceStream(name)!;
            using var output = new MemoryStream(); input.CopyTo(output); byte[] bytes = output.ToArray();
            foreach (var encoding in new[] { System.Text.Encoding.ASCII, System.Text.Encoding.BigEndianUnicode })
            {
                byte[] old = encoding.GetBytes("Roboto"); byte[] renamed = encoding.GetBytes("Relink");
                for (int i = 0; i <= bytes.Length - old.Length; i++)
                    if (bytes.AsSpan(i, old.Length).SequenceEqual(old)) renamed.CopyTo(bytes, i);
            }
            return bytes;
        }
        string selected = Path.Combine(directory, "z-selected.ttf");
        string conflict = Path.Combine(directory, "a-conflicting.ttf");
        string companion = Path.Combine(directory, "medium.ttf");
        string unreadable = Path.Combine(directory, "locked.ttf");
        File.WriteAllBytes(selected, ReadFont("ProjectFontSecondFixture.ttf"));
        File.Copy(selected, conflict);
        File.WriteAllBytes(companion, ReadFont("ProjectFontMediumFixture.ttf"));
        File.Copy(selected, unreadable);
        var family = new FontFamily("Relink");
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png"));
            element.Objects.Clear(); element.Objects.Add(new Beutl.Graphics.Shapes.TextBlock { FontFamily = { CurrentValue = family } });
        }
        try
        {
            using var locked = new FileStream(unreadable, FileMode.Open, FileAccess.Read, FileShare.None);
            using var vm = new MissingMediaViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(), selected);
            await vm.WaitForCandidatesAsync();
            Assert.That(vm.Rows.Single().ReplacementPath.Value, Is.EqualTo(selected));
            var wrongCase = new MissingMedia(MissingMediaKind.Font, null, new FontFamily("relink"), [], null);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new MissingMediaService().ValidateAsync(wrongCase, selected));
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            string bundled = Path.Combine(Path.GetDirectoryName(editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath)!, "resources", "fonts");
            Assert.That(Directory.GetFiles(bundled).Select(Path.GetFileName), Is.EquivalentTo(new[] { "z-selected.ttf", "medium.ttf" }));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Grouped_model_repairs_update_base_and_derived_sources()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        string original = Path.Combine(directory, "old.obj"); string replacement = Path.Combine(directory, "new.obj");
        File.WriteAllText(original, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        File.Copy(original, replacement);
        ModelSource[] sources = [new ModelSource(), new DerivedRepairModelSource()];
        using (editor.HistoryManager.SuppressRecording())
        {
            foreach (var source in sources)
            {
                source.ReadFrom(new Uri(original));
                var element = AddImage(editor.Scene, Path.Combine(directory, Guid.NewGuid() + ".png"));
                element.Objects.Clear(); element.Objects.Add(new Model3D { Source = { CurrentValue = source } });
            }
        }
        File.Delete(original);
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            Assert.That(vm.Rows, Has.Count.EqualTo(1));
            await vm.SetReplacementAsync(vm.Rows.Single(), replacement);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(sources.Select(source => source.Uri), Is.All.EqualTo(new Uri(replacement)));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Replacing_a_model_retires_only_imported_surplus_children_after_reopening()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        string original = Path.Combine(directory, "old.obj");
        string replacement = Path.Combine(directory, "new.obj");
        File.WriteAllText(original, "o First\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\no Second\nv 3 0 0\nv 5 0 0\nv 3 2 0\nf 4 5 6\n");
        File.WriteAllText(replacement, "v 0 0 0\nv 7 0 0\nv 0 3 0\nf 1 2 3\n");
        var source = new ModelSource(); source.ReadFrom(new Uri(original));
        var model = new Model3D { Source = { CurrentValue = source } };
        var first = model.Children[0];
        var retired = model.Children[1];
        model.Children.Move(0, 1);
        model.Children.Add(new MeshObject3D { Name = "User child", Mesh = { CurrentValue = new ModelMesh() } });
        first.Name = "Keep first edit";
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png"));
            element.Objects.Clear(); element.Objects.Add(model);
        }
        await editor.FlushMediaFingerprintsAsync();
        await editor.SaveAsync();
        string projectPath = editor.Scene.FindHierarchicalParent<Project>()!.Uri!.LocalPath;
        File.Delete(original);
        await TestReset.ResetShellAsync();
        await TestShell.Project.OpenProject(projectPath);
        TestShell.Editor.ActivateTabItem(TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().Single());
        editor = (Beutl.ViewModels.EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        model = (Model3D)editor.Scene.Children.Single().Objects.Single();
        var added = model.Children.Single(child => child.Name == "User child");
        string addedJson = CoreSerializer.SerializeToJsonObject(added).ToJsonString();
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(), replacement);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(model.Children.Select(child => child.Id), Does.Not.Contain(retired.Id));
            Assert.That(model.Children.Select(child => child.Id), Does.Contain(first.Id));
            Assert.That(model.Children, Has.Count.EqualTo(2));
            Assert.That(CoreSerializer.SerializeToJsonObject(added).ToJsonString(), Is.EqualTo(addedJson));
            Assert.That(CoreSerializer.RestoreFromUri<Scene>(editor.Scene.Uri!).Children.Single().Objects.OfType<Model3D>().Single().Children,
                Has.Count.EqualTo(2));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public void A_model_with_a_missing_required_buffer_opens_offline_with_saved_children()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "model.gltf");
        string buffer = Path.Combine(directory, "buffer.bin");
        File.WriteAllBytes(buffer, new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }.SelectMany(BitConverter.GetBytes).ToArray());
        File.WriteAllText(path, """
            {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
            "meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":4}]}],
            "buffers":[{"uri":"buffer.bin","byteLength":36}],"bufferViews":[{"buffer":0,"byteLength":36}],
            "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}]}
            """);
        var source = new ModelSource(); source.ReadFrom(new Uri(path));
        var model = new Model3D { Source = { CurrentValue = source } };
        model.Children[0].Name = "Saved mesh";
        var json = CoreSerializer.SerializeToJsonObject(model);
        File.Delete(buffer);
        var restored = (Model3D)CoreSerializer.DeserializeFromJsonObject(json, typeof(Model3D))!;
        Assert.That(restored.Source.CurrentValue!.Uri, Is.EqualTo(new Uri(path)));
        Assert.That(restored.Source.CurrentValue.MeshCount, Is.Zero);
        Assert.That(restored.Children.Single().Name, Is.EqualTo("Saved mesh"));
    }

    [AvaloniaTest]
    public async Task Refresh_and_a_second_folder_keep_pending_replacements_and_candidates()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string firstDirectory = NewDirectory(); string secondDirectory = NewDirectory();
        using (editor.HistoryManager.SuppressRecording())
            foreach (string name in new[] { "first.png", "candidate.png", "second.png" })
                AddImage(editor.Scene, Path.Combine(firstDirectory, "missing", name));
        foreach (string name in new[] { "first.png", "candidate.png" })
            File.WriteAllBytes(Path.Combine(firstDirectory, name), s_png);
        File.WriteAllBytes(Path.Combine(secondDirectory, "second.png"), s_png);
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            var chosen = vm.Rows.Single(row => row.Name == "first.png");
            var candidate = vm.Rows.Single(row => row.Name == "candidate.png");
            await vm.SetReplacementAsync(chosen, Path.Combine(firstDirectory, "first.png"));
            await vm.WaitForCandidatesAsync();
            candidate.IsExpanded.Value = true;
            await vm.RefreshRowsAsync();
            Assert.That(vm.Rows.Single(row => row.Name == "first.png"), Is.SameAs(chosen));
            Assert.That(chosen.ReplacementPath.Value, Is.EqualTo(Path.Combine(firstDirectory, "first.png")));
            Assert.That(candidate.IsExpanded.Value, Is.True);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Name == "second.png"), Path.Combine(secondDirectory, "second.png"));
            await vm.WaitForCandidatesAsync();
            Assert.That(candidate.CandidatePath.Value, Is.EqualTo(Path.Combine(firstDirectory, "candidate.png")));
            vm.UseCandidate(candidate);
            File.WriteAllText(candidate.ReplacementPath.Value!, "corrupt");
            Assert.That(await vm.ApplyAsync(), Is.False);
            Assert.That(chosen.Media.References.Single().Source!.Uri.LocalPath, Does.Contain("missing"));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Individual_repair_can_be_applied_while_candidate_discovery_is_waiting()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        using (editor.HistoryManager.SuppressRecording())
        {
            AddImage(editor.Scene, Path.Combine(directory, "missing", "first.png"));
            AddImage(editor.Scene, Path.Combine(directory, "missing", "other.png"));
        }
        string selected = Path.Combine(directory, "first.png"); File.WriteAllBytes(selected, s_png);
        var discovery = new TaskCompletionSource<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>>();
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            vm.FindCandidateMatches = (_, _, token) => discovery.Task.WaitAsync(token);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Name == "first.png"), selected);
            Assert.That(vm.IsBusy.Value, Is.False);
            Assert.That(vm.CanApply.Value, Is.True);
            Assert.That(discovery.Task.IsCompleted, Is.False);
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
            Assert.That(vm.Rows.Single().Name, Is.EqualTo("other.png"));
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task A_failed_repair_save_restores_sources_geometry_fingerprints_and_history()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        string directory = NewDirectory();
        string original = Path.Combine(directory, "old.obj"); string replacement = Path.Combine(directory, "new.obj");
        File.WriteAllText(original, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        File.WriteAllText(replacement, "v 0 0 0\nv 3 0 0\nv 0 2 0\nf 1 2 3\n");
        var source = new ModelSource(); source.ReadFrom(new Uri(original));
        var model = new Model3D { Source = { CurrentValue = source } };
        using (editor.HistoryManager.SuppressRecording())
        {
            var element = AddImage(editor.Scene, Path.Combine(directory, "unused.png"));
            element.Objects.Clear(); element.Objects.Add(model);
            AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
        }
        await editor.FlushMediaFingerprintsAsync(); await editor.SaveAsync(); await editor.WaitForMediaFingerprintsAsync();
        File.Delete(original);
        string before = CoreSerializer.SerializeToJsonObject(model).ToJsonString();
        var fingerprints = editor.Scene.MediaFingerprints.ToArray();
        string image = Path.Combine(directory, "image.png"); File.WriteAllBytes(image, s_png);
        try
        {
            using var vm = new MissingMediaViewModel(editor);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Model), replacement);
            await vm.SetReplacementAsync(vm.Rows.Single(row => row.Media.Kind == MissingMediaKind.Image), image);
            using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
                   { if (step == StorageWriteStep.Replace && path == editor.Scene.Uri!.LocalPath) throw new IOException("Injected repair save failure."); }))
                Assert.That(await vm.ApplyAsync(), Is.False);
            Assert.That(CoreSerializer.SerializeToJsonObject(model).ToJsonString(), Is.EqualTo(before));
            Assert.That(editor.Scene.MediaFingerprints, Is.EquivalentTo(fingerprints));
            Assert.That(editor.HasMediaRepairs.Value, Is.False);
            Assert.That(((Beutl.Graphics.SourceImage)editor.Scene.Children[1].Objects.Single()).Source.CurrentValue!.Uri.LocalPath,
                Is.EqualTo(Path.Combine(directory, "missing.png")));
            Assert.That(await vm.ApplyAsync(), Is.True, vm.Error.Value);
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Fingerprint_flush_logs_advisory_failure_but_preserves_cancellation()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        await editor.WaitForMediaFingerprintsAsync();
        try
        {
            var pending = new TaskCompletionSource();
            var started = new TaskCompletionSource();
            editor.CaptureMediaFingerprints = (_, _) => { started.SetResult(); return pending.Task; };
            editor.ScheduleMediaFingerprints(); await started.Task;
            Task flushing = editor.FlushMediaFingerprintsAsync();
            pending.SetException(new InvalidDataException("Advisory capture failed."));
            await flushing;
            editor.CaptureMediaFingerprints = (_, _) => Task.FromException(new InvalidDataException("Advisory capture failed."));
            await editor.FlushMediaFingerprintsAsync();
            Assert.That(await editor.SaveAsync(), Is.True);
            editor.CaptureMediaFingerprints = (_, _) => Task.FromCanceled(new CancellationToken(true));
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await editor.FlushMediaFingerprintsAsync());
        }
        finally { await TestReset.ResetShellAsync(); }
    }

    [AvaloniaTest]
    public async Task Timestamp_preserving_overwrites_get_a_new_fingerprint()
    {
        string directory = NewDirectory(); string path = Path.Combine(directory, "image.png");
        File.WriteAllBytes(path, s_png);
        var scene = new Scene(); AddImage(scene, path);
        var service = new MissingMediaService(); await service.UpdateFingerprintsAsync(scene);
        string first = scene.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256;
        DateTime time = File.GetLastWriteTimeUtc(path);
        byte[] bytes = s_png.ToArray(); bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time);
        await service.UpdateFingerprintsAsync(scene);
        Assert.That(scene.MediaFingerprints[new Uri(path).AbsoluteUri].Sha256, Is.Not.EqualTo(first));
    }

    [AvaloniaTest]
    public void Offline_resources_share_pixels_with_independent_disposal()
    {
        string directory = NewDirectory();
        var first = ImageSource.Open(Path.Combine(directory, "one.png")).ToResource(CompositionContext.Default);
        using var second = ImageSource.Open(Path.Combine(directory, "two.png")).ToResource(CompositionContext.Default);
        Assert.That(first.Bitmap!.Data, Is.EqualTo(second.Bitmap!.Data));
        first.Dispose();
        Assert.That(second.Bitmap.IsDisposed, Is.False);
        Assert.That(second.Bitmap.Data, Is.Not.EqualTo(IntPtr.Zero));
    }

    [AvaloniaTest]
    public async Task Ordinary_property_repairs_dismiss_the_missing_media_warning()
    {
        await TestReset.ResetShellAsync();
        var editor = await CreateEditorAsync();
        var previous = NotificationService.Handler; var handler = new CaptureNotificationHandler();
        NotificationService.Handler = handler;
        try
        {
            string directory = NewDirectory();
            var element = AddImage(editor.Scene, Path.Combine(directory, "missing.png"));
            editor.NotifyMissingMedia(); await editor.WaitForMissingMediaAsync();
            var notification = handler.Notifications.Single();
            string valid = Path.Combine(directory, "valid.png"); File.WriteAllBytes(valid, s_png);
            ((Beutl.Graphics.SourceImage)element.Objects.Single()).Source.CurrentValue = ImageSource.Open(valid);
            editor.HistoryManager.Commit("Replace image");
            await editor.WaitForMissingMediaAsync();
            Assert.That(notification.CancellationToken.IsCancellationRequested, Is.True);
        }
        finally { await TestReset.ResetShellAsync(); NotificationService.Handler = previous; }
    }
}

public sealed class DerivedRepairModelSource : ModelSource;
