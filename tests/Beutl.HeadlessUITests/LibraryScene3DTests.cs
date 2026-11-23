using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Primitives;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryScene3DTests
{
    [AvaloniaTest]
    public async Task Adding_a_library_scene3d_puts_its_cube_in_the_portal_and_undoes_both()
    {
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("library-scene3d");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        MediaTime length = MediaTime.FromSeconds(2);
        ElementAddResult added = await adder.AddAsync(
            [LibraryElementDescription.Create(typeof(Scene3D), MediaTime.FromSeconds(1), length, 0)], CancellationToken.None);

        Assert.That(added.IsSuccess, Is.True);
        Assert.That(scene.Children, Has.Count.EqualTo(2));
        Element primary = scene.Children.Single(element => element.Objects.OfType<Scene3D>().Any());
        Element cube = scene.Children.Single(element => element.Objects.OfType<Cube3D>().Any());
        Assert.That(primary.Objects.OfType<PortalObject>().Single().Count.Value, Is.EqualTo(1));
        Assert.That(cube.ZIndex.Value, Is.EqualTo(primary.ZIndex.Value + 1));
        Assert.That(cube.Range, Is.EqualTo(primary.Range));
        Assert.That(await editor.UndoAsync(), Is.True);
        Assert.That(scene.Children, Is.Empty);

        added = await adder.AddAsync(
            [new ElementDescription(MediaTime.Zero, length, 0, new ElementSource.EngineObject(() => new Scene3D()))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);
        Assert.That(scene.Children, Has.Count.EqualTo(1));
        Assert.That(scene.Children[0].Objects.OfType<PortalObject>().Single().Count.Value, Is.Zero);
    }
}
