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

    [AvaloniaTest]
    public void The_host_follows_the_open_project_and_its_palettes()
    {
        var current = new BehaviorSubject<Project?>(null);
        using var host = new ProjectColorPaletteHost(current, () => new Lease(), (_, _) => { });
        int changes = 0;
        host.PalettesChanged += (_, _) => changes++;
        Assert.That(host.Palettes, Is.Null);

        Project project = CreateProject();
        project.ColorPalettes.Value = s_brand;
        current.OnNext(project);
        Assert.That(host.Palettes, Is.EqualTo(s_brand));

        project.ColorPalettes.Value = [];
        Assert.That(host.Palettes, Is.Empty);

        current.OnNext(null);
        Assert.That(host.Palettes, Is.Null);
        Assert.That(changes, Is.EqualTo(3));
    }

    [AvaloniaTest]
    public void An_edit_is_written_into_the_project_file_at_once_under_a_write_lease()
    {
        Project project = CreateProject();
        var lease = new Lease();
        var writes = new List<(Uri Uri, ImmutableArray<ColorPalette> Palettes, bool Leased)>();
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => lease,
            (written, uri) => writes.Add((uri, written.ColorPalettes.Value, !lease.Disposed)));

        Assert.That(host.TrySetPalettes(s_brand), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(writes, Has.Count.EqualTo(1));
            Assert.That(writes[0].Uri, Is.EqualTo(project.Uri));
            Assert.That(writes[0].Palettes, Is.EqualTo(s_brand));
            Assert.That(writes[0].Leased, Is.True, "The write happens while the lease is held.");
            Assert.That(lease.Disposed, Is.True, "The lease is released after the write.");
            Assert.That(project.ColorPalettes.Value, Is.EqualTo(s_brand));
        });
    }

    [AvaloniaTest]
    public void A_failed_write_puts_the_palettes_back()
    {
        Project project = CreateProject();
        project.ColorPalettes.Value = s_brand;
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => new Lease(),
            (_, _) => throw new IOException("disk full"));

        Assert.That(host.TrySetPalettes([]), Is.False);
        Assert.That(project.ColorPalettes.Value, Is.EqualTo(s_brand));
    }

    // A branch switch, pull or restore holds the workspace, and the palette waits rather than writing under it.
    [AvaloniaTest]
    public void A_busy_workspace_writes_nothing()
    {
        Project project = CreateProject();
        int writes = 0;
        using var host = new ProjectColorPaletteHost(
            new BehaviorSubject<Project?>(project),
            () => null,
            (_, _) => writes++);

        Assert.That(host.TrySetPalettes(s_brand), Is.False);
        Assert.That(writes, Is.Zero);
        Assert.That(project.ColorPalettes.Value, Is.Empty);
    }

    // A palette edit writes the project file alone; the scenes keep their unsaved edits for the user to save.
    [AvaloniaTest]
    public void The_project_file_is_written_without_its_scenes()
    {
        string root = Path.Combine(Path.GetTempPath(), "beutl-palette-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scene = new Scene(640, 480, "Saved") { Uri = new Uri(Path.Combine(root, "Scene.scene")) };
            var project = new Project { Uri = new Uri(Path.Combine(root, "Project.bep")) };
            project.Items.Add(scene);
            CoreSerializer.StoreToUri(project, project.Uri!);
            scene.Name.Value = "Unsaved";
            project.ColorPalettes.Value = s_brand;

            ProjectColorPaletteHost.StoreProjectFile(project, project.Uri!);

            Assert.That(File.ReadAllText(scene.Uri!.LocalPath), Does.Contain("Saved").And.Not.Contain("Unsaved"));
            Assert.That(File.ReadAllText(project.Uri!.LocalPath), Does.Contain("\"Brand\""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Project CreateProject()
    {
        return new Project { Uri = new Uri(Path.Combine(Path.GetTempPath(), "palette-host-test", "project.bep")) };
    }

    private sealed class Lease : IProjectFileWriteLease
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
