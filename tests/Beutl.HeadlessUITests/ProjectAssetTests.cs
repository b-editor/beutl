using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Graphics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Models;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class ProjectAssetTests
{
    private static ProjectAssetCoordinator Assets => TestShell.Editor.ProjectAssets!;

    [AvaloniaTest]
    public async Task Collection_saves_all_affected_scenes_and_the_project_can_move()
    {
        (Project project, Scene scene, _) = await TestShell.OpenNewSceneAsync("Collection", width: 64, height: 64);
        string root = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string sources = Path.Combine(Path.GetDirectoryName(root)!, "outside");
        string first = MakeImage(Path.Combine(sources, "first", "photo #1.png"), SKColors.Red);
        string second = MakeImage(Path.Combine(sources, "second", "photo #1.png"), SKColors.Blue);
        var unopened = new Scene(64, 64, "Unopened") { Uri = new Uri(Path.Combine(root, "unopened", "unopened.scene")) };
        using (TestShell.Editor.SuppressProjectItemActivation()) project.Items.Add(unopened);
        ImageSource inUnopened = AddImage(unopened, first, enabled: false);
        CoreSerializer.StoreToUri(project, project.Uri);

        bool autoSave = EditViewModel.IsAutoSaveSuppressedForTesting;
        EditViewModel.IsAutoSaveSuppressedForTesting = true;
        try
        {
            ImageSource live = AddImage(scene, first);
            ImageSource different = AddImage(scene, second);
            ProjectAssetPreview preview = await Assets.PreviewAsync(project, CancellationToken.None);

            Assert.That(preview.External, Has.Length.EqualTo(2));
            Assert.That(Directory.Exists(Path.Combine(root, "resources")), Is.False, "Preview must be read-only.");
            await Assets.ApplyAsync(project, preview, ProjectAssetAction.Collect, preview.External, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(live.Uri, Is.EqualTo(inUnopened.Uri));
                Assert.That(live.Uri, Is.Not.EqualTo(different.Uri));
                Assert.That(File.ReadAllBytes(live.Uri.LocalPath), Is.EqualTo(File.ReadAllBytes(first)));
                Assert.That(File.ReadAllBytes(different.Uri.LocalPath), Is.EqualTo(File.ReadAllBytes(second)));
                Assert.That(TestShell.Project.CurrentProject.Value, Is.SameAs(project));
            });
            Assert.That(CoreSerializer.RestoreFromUri<Project>(project.Uri).Items.OfType<Scene>()
                .SelectMany(MissingMediaService.GetFileUris).Distinct(), Is.EquivalentTo(new[] { live.Uri, different.Uri }));
            Assert.That((await Assets.PreviewAsync(project, CancellationToken.None)).External, Is.Empty);

            string fileName = Path.GetFileName(project.Uri.LocalPath);
            await TestShell.Project.CloseProjectAsync();
            Directory.Move(sources, sources + "-offline");
            string moved = root + "-moved";
            Directory.Move(root, moved);
            Project reopened = CoreSerializer.RestoreFromUri<Project>(new Uri(Path.Combine(moved, fileName)));
            Uri[] media = [.. reopened.Items.OfType<Scene>().SelectMany(MissingMediaService.GetFileUris).Distinct()];
            Assert.That(media, Has.Length.EqualTo(2));
            Assert.That(media.All(uri => ProjectAssetPaths.IsBelow(moved, uri.LocalPath)), Is.True);
            Assert.That(media.Select(uri => ReadColor(uri.LocalPath)), Is.EquivalentTo(new[] { SKColors.Red, SKColors.Blue }));
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = autoSave;
        }
    }

    [AvaloniaTest]
    public async Task Collection_copies_model_dependencies_and_keeps_edited_geometry()
    {
        (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("Asset model");
        var paths = new ProjectAssetPaths(project);
        string outside = Path.Combine(Path.GetDirectoryName(paths.Root)!, "model-source");
        Directory.CreateDirectory(outside);
        MakeImage(Path.Combine(outside, "texture.png"), SKColors.Coral);
        File.WriteAllText(Path.Combine(outside, "surface.mtl"), "newmtl surface\nmap_Kd texture.png\n");
        string original = Path.Combine(outside, "triangle.obj");
        File.WriteAllText(original, "mtllib surface.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl surface\nf 1 2 3\n");
        var source = new ModelSource();
        source.ReadFrom(new Uri(original));
        var model = new Model3D { Source = { Value = source } };
        Object3D editedChild = model.Children.Single();
        editedChild.Name.Value = "Edited triangle";
        var scene3D = new Scene3D();
        scene3D.Objects.Add(model);
        var element = new Element
        {
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "model.belm")),
            Length = { Value = MediaTime.FromSeconds(1) },
        };
        element.AddObject(scene3D);
        scene.Children.Add(element);
        await TestShell.WaitForProjectWritesAsync();
        Assert.That(await editor.SaveAsync(), Is.True);

        ProjectAssetPreview preview = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(preview.External.Single().Files.Select(file => Path.GetFileName(file.Path)),
            Is.EquivalentTo(new[] { "triangle.obj", "surface.mtl", "texture.png" }));
        await Assets.ApplyAsync(project, preview, ProjectAssetAction.Collect, preview.External, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(model.Children.Single(), Is.SameAs(editedChild));
            Assert.That(model.Children.Single().Name.Value, Is.EqualTo("Edited triangle"));
            Assert.That(source.Dependencies, Is.All.StartsWith(paths.Resources + Path.DirectorySeparatorChar));
            Assert.That(File.Exists(original), Is.True);
        });
        Assert.That((await Assets.PreviewAsync(project, CancellationToken.None)).Unused, Is.Empty);
    }

    [AvaloniaTest]
    public async Task Cleanup_protects_model_dependencies_referenced_only_by_a_saved_element()
    {
        (Project project, _, _) = await TestShell.OpenNewSceneAsync("Saved model dependencies");
        var paths = new ProjectAssetPaths(project);
        string modelDirectory = Path.Combine(paths.Resources, "model");
        string textureFile = MakeImage(Path.Combine(modelDirectory, "texture.png"), SKColors.Coral);
        string materialFile = Path.Combine(modelDirectory, "surface.mtl");
        File.WriteAllText(materialFile, "newmtl surface\nmap_Kd texture.png\n");
        string modelFile = Path.Combine(modelDirectory, "triangle.obj");
        File.WriteAllText(modelFile, "mtllib surface.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl surface\nf 1 2 3\n");
        string unused = MakeImage(Path.Combine(paths.Resources, "unused.png"), SKColors.Green);
        ProjectAssetPreview beforeSave = await Assets.PreviewAsync(project, CancellationToken.None);
        ProjectAssetItem material = beforeSave.Unused.Single(item => item.Path == materialFile);
        Assert.That(material.CanApply(ProjectAssetAction.Quarantine), Is.True);

        var source = new ModelSource();
        source.ReadFrom(new Uri(modelFile));
        var model = new Model3D { Source = { Value = source } };
        model.Children.Clear(); // Leave the texture referenced only by the model's material file.
        var scene3D = new Scene3D();
        scene3D.Objects.Add(model);
        var element = new Element { Uri = new Uri(Path.Combine(paths.Root, "archived.belm")) };
        element.AddObject(scene3D);
        CoreSerializer.StoreToUri(element, element.Uri);

        ProjectAssetPreview saved = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(saved.Unused.Select(item => item.Path), Is.EqualTo(new[] { unused }));
        await Assert.ThrowsAsync<ProjectAssetPlanChangedException>(() => Assets.ApplyAsync(project, beforeSave,
            ProjectAssetAction.Quarantine, [material], CancellationToken.None));
        Assert.That(File.Exists(materialFile) && File.Exists(textureFile), Is.True);

        File.Delete(modelFile);
        ProjectAssetPreview missing = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(missing.CleanupIssue, Is.EqualTo(ProjectAssetIssue.IncompleteReferences));
        Assert.That(missing.Unused.Single(item => item.Path == unused).CanApply(ProjectAssetAction.Quarantine), Is.False);
    }

    [AvaloniaTest]
    public async Task Save_failure_restores_uris_and_documents_and_discards_new_copies()
    {
        (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("Asset rollback");
        string root = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string original = MakeImage(Path.Combine(Path.GetDirectoryName(root)!, "external.png"), SKColors.Coral);
        ImageSource source = AddImage(scene, original);
        await TestShell.WaitForProjectWritesAsync();
        Assert.That(await editor.SaveAsync(), Is.True);
        string elementFile = scene.Children.Single().Uri!.LocalPath;
        byte[] previous = File.ReadAllBytes(elementFile);
        ProjectAssetPreview preview = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(preview.External.Single().Path, Is.EqualTo(original));

        string projectFile = project.Uri!.LocalPath;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == projectFile)
                       throw new IOException("injected project asset save failure");
               }))
        {
            await Assert.ThrowsAsync<IOException>(() => Assets.ApplyAsync(project, preview, ProjectAssetAction.Collect,
                preview.External, CancellationToken.None));
        }

        Assert.Multiple(() =>
        {
            Assert.That(source.Uri.LocalPath, Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(elementFile), Is.EqualTo(previous));
            Assert.That(File.Exists(preview.External.Single().Destination), Is.False);
            Assert.That(editor.IsEnabled.Value, Is.True);
        });
        await Assets.ApplyAsync(project, preview, ProjectAssetAction.Collect, preview.External, CancellationToken.None);
        Assert.That(File.Exists(source.Uri.LocalPath), Is.True);
    }

    [AvaloniaTest]
    public async Task Cleanup_rechecks_usage_and_can_restore_the_original_path_after_reopening()
    {
        (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("Asset cleanup");
        string resources = new ProjectAssetPaths(project).Resources;
        string used = MakeImage(Path.Combine(resources, "disabled.png"), SKColors.Red);
        string history = MakeImage(Path.Combine(resources, "old.png"), SKColors.Blue);
        string unused = MakeImage(Path.Combine(resources, "nested", "unused.png"), SKColors.Green);
        string changed = MakeImage(Path.Combine(resources, "changed.png"), SKColors.AliceBlue);
        string fontFolderFile = MakeImage(Path.Combine(resources, "fonts", "protected.png"), SKColors.Gray);
        AddImage(scene, used, enabled: false);
        AddImage(scene, history);
        editor.HistoryManager.Commit();
        var drawable = scene.Children.Last().Objects.OfType<SourceImage>().Single();
        drawable.Source.Value = NewImage(used);
        editor.HistoryManager.Commit();
        await TestShell.WaitForProjectWritesAsync();
        Assert.That(await editor.SaveAsync(), Is.True);

        ProjectAssetPreview preview = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(preview.Unused.Select(item => (item.Path, item.Issue)), Is.EquivalentTo(new[]
        {
            (history, (ProjectAssetIssue?)ProjectAssetIssue.EditHistory),
            (unused, null),
            (changed, null),
        }));
        AddImage(scene, changed);
        await Assert.ThrowsAsync<ProjectAssetPlanChangedException>(() => Assets.ApplyAsync(project, preview,
            ProjectAssetAction.Quarantine, preview.Unused.Where(item => item.Issue == null).ToArray(), CancellationToken.None));
        Assert.That(File.Exists(unused), Is.True);

        await Assets.ApplyAsync(project, preview, ProjectAssetAction.Quarantine,
            [preview.Unused.Single(item => item.Path == unused)], CancellationToken.None);
        Assert.That(File.Exists(unused), Is.False);
        Uri projectUri = project.Uri!;
        await TestShell.Project.CloseProjectAsync();
        await TestShell.Project.OpenProject(projectUri.LocalPath);
        project = TestShell.Project.CurrentProject.Value!;
        ProjectAssetPreview reopened = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(reopened.Quarantined.Single().Destination, Is.EqualTo(unused));
        MakeImage(unused, SKColors.Red);
        ProjectAssetPreview occupied = await Assets.PreviewAsync(project, CancellationToken.None);
        Assert.That(occupied.Quarantined.Single().Issue, Is.EqualTo(ProjectAssetIssue.DestinationExists));
        await Assert.ThrowsAsync<ProjectAssetPlanChangedException>(() => Assets.ApplyAsync(project, reopened,
            ProjectAssetAction.Restore, reopened.Quarantined, CancellationToken.None));
        Assert.That(ReadColor(unused), Is.EqualTo(SKColors.Red));
        File.Delete(unused);
        await Assets.ApplyAsync(project, reopened, ProjectAssetAction.Restore, reopened.Quarantined, CancellationToken.None);
        Assert.That(ReadColor(unused), Is.EqualTo(SKColors.Green));

        ProjectAssetPreview restored = await Assets.PreviewAsync(project, CancellationToken.None);
        await Assets.ApplyAsync(project, restored, ProjectAssetAction.Quarantine,
            [restored.Unused.Single(item => item.Path == unused)], CancellationToken.None);
        ProjectAssetPreview beforeDelete = await Assets.PreviewAsync(project, CancellationToken.None);
        await Assets.ApplyAsync(project, beforeDelete, ProjectAssetAction.Delete, beforeDelete.Quarantined, CancellationToken.None);
        Assert.That(File.Exists(beforeDelete.Quarantined.Single().Path), Is.False);
        Assert.That(File.Exists(used) && File.Exists(history) && File.Exists(changed) && File.Exists(fontFolderFile), Is.True);
    }

    [AvaloniaTest]
    public async Task Both_menus_open_assets_inside_the_file_tab_without_storage_providers()
    {
        await TestReset.ResetShellAsync();
        var providers = TestShell.Editor.StorageProviders;
        TestShell.Editor.StorageProviders = null;
        var main = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = main, Width = 1120, Height = 840 };
        try
        {
            (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("Sample project", reset: false);
            var paths = new ProjectAssetPaths(project);
            string sourceDirectory = Path.GetDirectoryName(paths.Root)!;
            AddImage(scene, MakeImage(Path.Combine(sourceDirectory, "Cloudscape.png"), SKColors.CornflowerBlue));
            AddImage(scene, Path.Combine(sourceDirectory, "Missing still.png"), enabled: false);
            string damaged = Path.Combine(sourceDirectory, "Damaged take.png");
            File.WriteAllText(damaged, "unreadable image");
            AddImage(scene, damaged, enabled: false);
            string unused = MakeImage(Path.Combine(paths.Resources, "Unused take.png"), SKColors.SeaGreen);
            window.Show();
            HeadlessTestHelpers.Render();
            var fileMenu = main.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Strings.File));
            var command = fileMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, ProjectAssetsStrings.MenuLabel)).Command!;
            Assert.That(command.CanExecute(null), Is.True);
            command.Execute(null);
            FileBrowserTabViewModel files = editor.FindToolTab<FileBrowserTabViewModel>(tab => tab.IsProjectAssetsView.Value)!;
            Assert.That(files, Is.Not.Null);
            Assert.That(files.HasStorageProviders, Is.False);
            (FileBrowserTabView browser, ProjectAssetsView view, ProjectAssetsViewModel vm) = await WaitForAssetsViewAsync(window, files);
            Assert.That(vm.Project, Is.SameAs(project));
            var selectAll = view.FindControl<CheckBox>("SelectAllAssets")!;
            Assert.That(selectAll.IsThreeState, Is.True);
            Assert.That(selectAll.IsChecked, Is.True, "A missing asset does not count toward the selectable rows.");
            vm.Rows.Value.Single(row => row.Item.Path == damaged).IsSelected = false;
            HeadlessTestHelpers.Render();
            Assert.That(selectAll.IsChecked, Is.Null);
            HeadlessCapture.SaveIfRequested(view, nameof(ProjectAssetTests), "file-tab-partial-selection");
            HeadlessTestHelpers.Click(selectAll);
            Assert.That(selectAll.IsChecked, Is.True);
            HeadlessTestHelpers.Click(selectAll);
            Assert.That(selectAll.IsChecked, Is.False);
            HeadlessTestHelpers.Click(selectAll);
            Assert.That(selectAll.IsChecked, Is.True);
            HeadlessCapture.SaveIfRequested(view, nameof(ProjectAssetTests), "file-tab-collect");
            foreach (ProjectAssetRow row in vm.Rows.Value) row.IsSelected = row.Item.Path == damaged;
            HeadlessTestHelpers.Render();
            HeadlessTestHelpers.Click(view.FindControl<Button>("ApplyAssetsButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => !vm.IsBusy.Value && vm.Error.Value != null);
            Assert.That(File.Exists(Path.Combine(paths.Resources, Path.GetFileName(damaged))), Is.False);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(view, nameof(ProjectAssetTests), "file-tab-error");

            HeadlessTestHelpers.Click(browser.FindControl<Button>("CloseProjectAssetsButton")!);
            Assert.That(files.IsProjectAssetsView.Value, Is.False);
            Assert.That(files.ShowLocalBrowser.Value, Is.True);

            var button = browser.FindControl<Button>("StorageLocationsButton")!;
            Assert.That(button.IsEffectivelyVisible, Is.True);
            HeadlessTestHelpers.Click(button, move: true);
            var menu = (MenuFlyout)button.Flyout!;
            Assert.That(menu.IsOpen, Is.True);
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, ProjectAssetsStrings.MenuLabel))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            (_, view, vm) = await WaitForAssetsViewAsync(window, files);
            Assert.That(vm.Rows.Value.Select(row => row.Item.Issue), Is.EquivalentTo(new ProjectAssetIssue?[] { null, null, ProjectAssetIssue.Missing }));

            browser.FindControl<ComboBox>("AssetMode")!.SelectedIndex = 1;
            ProjectAssetRow beforeRefresh = vm.Rows.Value.Single();
            HeadlessTestHelpers.Render();
            HeadlessTestHelpers.Click(browser.FindControl<Button>("RefreshProjectAssetsButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => !vm.IsBusy.Value && !ReferenceEquals(vm.Rows.Value.Single(), beforeRefresh));
            vm.Rows.Value.Single().IsSelected = true;
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(view, nameof(ProjectAssetTests), "file-tab-unused");
            HeadlessTestHelpers.Click(view.FindControl<Button>("ApplyAssetsButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => !vm.IsBusy.Value && !File.Exists(unused));
            Assert.That(vm.Error.Value, Is.Null);
            browser.FindControl<ComboBox>("AssetMode")!.SelectedIndex = 2;
            vm.Rows.Value.Single().IsSelected = true;
            HeadlessTestHelpers.Render();
            HeadlessTestHelpers.Click(view.FindControl<Button>("SecondaryAssetsButton")!);
            Assert.That(vm.DeleteConfirmation.Value, Is.Not.Null);
            Assert.That(File.Exists(vm.Rows.Value.Single().Item.Path), Is.True);
            HeadlessCapture.SaveIfRequested(view, nameof(ProjectAssetTests), "file-tab-delete-confirmation");
            HeadlessTestHelpers.Click(view.FindControl<Button>("SecondaryAssetsButton")!);
            HeadlessTestHelpers.Render();
            HeadlessTestHelpers.Click(view.FindControl<Button>("ApplyAssetsButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => !vm.IsBusy.Value && File.Exists(unused));
            Assert.That(vm.Rows.Value, Is.Empty);
        }
        finally
        {
            TestShell.Editor.StorageProviders = providers;
            main.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static async Task<(FileBrowserTabView Browser, ProjectAssetsView View, ProjectAssetsViewModel ViewModel)> WaitForAssetsViewAsync(
        Window window, FileBrowserTabViewModel files)
    {
        FileBrowserTabView? browser = null;
        ProjectAssetsViewModel? viewModel = null;
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            browser = window.GetVisualDescendants().OfType<FileBrowserTabView>().FirstOrDefault(view => ReferenceEquals(view.DataContext, files));
            viewModel = files.ActiveProjectAssets.Value;
            return browser?.IsEffectivelyVisible == true && viewModel is { IsBusy.Value: false } && viewModel.Rows.Value.Length > 0;
        });
        return (browser!, browser!.FindControl<ProjectAssetsView>("ProjectAssetsContent")!, viewModel!);
    }

    private static string MakeImage(string path, SKColor color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static SKColor ReadColor(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return bitmap.GetPixel(0, 0);
    }

    private static ImageSource AddImage(Scene scene, string path, bool enabled = true)
    {
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        Directory.CreateDirectory(directory);
        ImageSource source = NewImage(path);
        var drawable = new SourceImage { Source = { Value = source } };
        var element = new Element
        {
            Uri = new Uri(Path.Combine(directory, $"{Guid.NewGuid():N}.belm")),
            Length = { Value = MediaTime.FromSeconds(1) },
            IsEnabled = { Value = enabled },
        };
        element.AddObject(drawable);
        scene.Children.Add(element);
        return source;
    }

    private static ImageSource NewImage(string path)
    {
        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateFromPath(path));
        return source;
    }
}
