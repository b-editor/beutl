using System.Collections.Immutable;
using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class ProjectColorPaletteHostTests
{
    private static readonly ImmutableArray<ColorPalette> s_brand = [new ColorPalette("Brand", [new PaletteColor(0xFF3366FF)])];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "beutl-palette-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [AvaloniaTest]
    public void The_host_follows_the_open_project_and_reads_its_palette_file()
    {
        Project project = CreateProject("Brand");
        ProjectColorPaletteFile.Write(PalettePath(project), s_brand);
        var current = new BehaviorSubject<Project?>(null);
        using var host = new ProjectColorPaletteHost(current, () => new Lease(), (_, _) => { });
        int changes = 0;
        host.PalettesChanged += (_, _) => changes++;
        Assert.That(host.Palettes, Is.Null);

        current.OnNext(project);
        Assert.That(host.Palettes, Is.EqualTo(s_brand));

        current.OnNext(CreateProject("Plain"));
        Assert.That(host.Palettes, Is.Empty);

        current.OnNext(null);
        Assert.That(host.Palettes, Is.Null);
        Assert.That(changes, Is.EqualTo(3));
    }

    [AvaloniaTest]
    public void An_edit_is_written_to_the_palette_file_at_once_under_a_write_lease()
    {
        Project project = CreateProject("Project");
        var lease = new Lease();
        var writes = new List<(string Path, ImmutableArray<ColorPalette> Palettes, bool Leased)>();
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => lease,
            (path, palettes) => writes.Add((path, palettes, !lease.Disposed)));

        Assert.That(host.TrySetPalettes(s_brand), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(writes, Has.Count.EqualTo(1));
            Assert.That(writes[0].Path, Is.EqualTo(PalettePath(project)));
            Assert.That(writes[0].Palettes, Is.EqualTo(s_brand));
            Assert.That(writes[0].Leased, Is.True, "The write happens while the lease is held.");
            Assert.That(lease.Disposed, Is.True, "The lease is released after the write.");
            Assert.That(host.Palettes, Is.EqualTo(s_brand));
        });
    }

    [AvaloniaTest]
    public void A_failed_write_keeps_the_palettes()
    {
        Project project = CreateProject("Project");
        ProjectColorPaletteFile.Write(PalettePath(project), s_brand);
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => new Lease(),
            (_, _) => throw new IOException("disk full"));

        Assert.That(host.TrySetPalettes([]), Is.False);
        Assert.That(host.Palettes, Is.EqualTo(s_brand));
    }

    // A branch switch, pull or restore holds the workspace, and the palette waits rather than writing under it.
    [AvaloniaTest]
    public void A_busy_workspace_writes_nothing()
    {
        Project project = CreateProject("Project");
        int writes = 0;
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => null,
            (_, _) => writes++);

        Assert.That(host.TrySetPalettes(s_brand), Is.False);
        Assert.That(writes, Is.Zero);
        Assert.That(host.Palettes, Is.Empty);
    }

    // The project file holds only what the project renders, so an edit leaves it, and the scenes, as they are.
    [AvaloniaTest]
    public void Palettes_are_written_beside_the_project_file_and_not_into_it()
    {
        Project project = CreateProject("Project");
        var scene = new Scene(640, 480, "Saved") { Uri = new Uri(Path.Combine(Path.GetDirectoryName(project.Uri!.LocalPath)!, "Scene.scene")) };
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri!);
        string projectFile = File.ReadAllText(project.Uri!.LocalPath);
        string sceneFile = File.ReadAllText(scene.Uri!.LocalPath);
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => new Lease(),
            ProjectColorPaletteFile.Write);

        Assert.That(host.TrySetPalettes(s_brand), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(project.Uri!.LocalPath), Is.EqualTo(projectFile));
            Assert.That(File.ReadAllText(scene.Uri!.LocalPath), Is.EqualTo(sceneFile));
            Assert.That(ProjectColorPaletteFile.Read(PalettePath(project), out bool canWrite), Is.EqualTo(s_brand));
            Assert.That(canWrite, Is.True);
        });
    }

    [AvaloniaTest]
    public void Removing_the_last_palette_removes_the_file()
    {
        Project project = CreateProject("Project");
        ProjectColorPaletteFile.Write(PalettePath(project), s_brand);
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => new Lease(),
            ProjectColorPaletteFile.Write);

        Assert.That(host.TrySetPalettes([]), Is.True);
        Assert.That(File.Exists(PalettePath(project)), Is.False);
    }

    // A later version's file, or one a merge left conflict markers in, shows no palettes and is never written over.
    [AvaloniaTest]
    public void A_file_this_version_cannot_read_is_left_as_it_is()
    {
        string[] contents =
        [
            "{\"Version\":2,\"Palettes\":[]}",
            "<<<<<<< ours\n{\"Version\":1}\n=======\n{\"Version\":1}\n>>>>>>> theirs\n",
        ];
        for (int i = 0; i < contents.Length; i++)
        {
            Project project = CreateProject($"Unreadable{i}");
            File.WriteAllText(PalettePath(project), contents[i]);
            int writes = 0;
            using var host = new ProjectColorPaletteHost(
                new BehaviorSubject<Project?>(project),
                () => new Lease(),
                (_, _) => writes++);

            Assert.That(host.Palettes, Is.Empty);
            Assert.That(host.TrySetPalettes(s_brand), Is.False);
            Assert.That(writes, Is.Zero);
            Assert.That(File.ReadAllText(PalettePath(project)), Is.EqualTo(contents[i]));
        }
    }

    private Project CreateProject(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return new Project { Uri = new Uri(Path.Combine(folder, $"{name}.bep")) };
    }

    private static string PalettePath(Project project) => ProjectColorPaletteFile.GetPath(project.Uri!.LocalPath);

    private sealed class Lease : IProjectFileWriteLease
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
